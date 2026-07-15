using System.Collections.Concurrent;
using System.Data;
using Microsoft.EntityFrameworkCore;
using BookDistributionAPI.Common;
using BookDistributionAPI.Data;

namespace BookDistributionAPI.Features.ReceiptVouchers;

public class ReceiptVoucherBusinessService
{
    private readonly AppDbContext _db;
    private readonly IAcademicYearHelper _academicYearHelper;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private static readonly ConcurrentDictionary<string, DateTime> _lockTimestamps = new();
    private static readonly TimeSpan LockCleanupInterval = TimeSpan.FromMinutes(30);
    private static DateTime _lastCleanup = DateTime.UtcNow;

    public ReceiptVoucherBusinessService(AppDbContext db, IAcademicYearHelper academicYearHelper)
    {
        _db = db;
        _academicYearHelper = academicYearHelper;
    }

    private static void CleanupStaleLocks()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastCleanup) < LockCleanupInterval)
            return;
        _lastCleanup = now;

        var cutoff = now.Add(-LockCleanupInterval);
        foreach (var kvp in _lockTimestamps)
        {
            if (kvp.Value < cutoff && _locks.TryGetValue(kvp.Key, out var sem))
            {
                if (sem.CurrentCount == 1)
                {
                    _locks.TryRemove(kvp.Key, out _);
                    _lockTimestamps.TryRemove(kvp.Key, out _);
                    sem.Dispose();
                }
            }
        }
    }

    public async Task<int> GetNextVoucherNumberAsync(int voucherYear, CancellationToken cancellationToken = default)
    {
        var maxNumber = await _db.ReceiptVouchers
            .Where(rv => rv.VoucherYear == voucherYear)
            .MaxAsync(rv => (int?)rv.VoucherNumber, cancellationToken) ?? 0;
        return maxNumber + 1;
    }

    public async Task<ReceiptVoucher> CreateAsync(CreateReceiptVoucherDto dto, CancellationToken cancellationToken = default)
    {
        var library = await _db.Libraries.FindAsync([dto.LibraryId], cancellationToken)
            ?? throw new InvalidOperationException("المكتبة غير موجودة");

        if (!library.IsActive)
            throw new InvalidOperationException("لا يمكن إنشاء سند قبض لمكتبة غير نشطة");

        if (dto.SemesterId.HasValue)
        {
            var semesterExists = await _db.Semesters.AnyAsync(s => s.Id == dto.SemesterId.Value, cancellationToken);
            if (!semesterExists)
                throw new InvalidOperationException("الفصل الدراسي غير موجود");

            var hasClearance = await _db.Invoices.AnyAsync(i =>
                i.LibraryId == dto.LibraryId &&
                i.SemesterId == dto.SemesterId.Value &&
                i.Type == "clearance", cancellationToken);
            if (hasClearance)
                throw new InvalidOperationException("لا يمكن إنشاء سند قبض بعد إصدار المخالصة النهائية");
        }

        if (dto.PaymentMethod == "cheque" && string.IsNullOrWhiteSpace(dto.ChequeNumber))
            throw new InvalidOperationException("يجب إدخال رقم الشيك عند الدفع بشيك");

        if (dto.Date.Year < 2000 || dto.Date.Year > 2100)
            throw new InvalidOperationException("التاريخ غير صحيح");

        CleanupStaleLocks();

        var lockKey = $"voucher-year:{dto.Date.Year}";
        var semaphore = _locks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        _lockTimestamps[lockKey] = DateTime.UtcNow;
        await semaphore.WaitAsync(cancellationToken);
        ReceiptVoucher voucher;
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

            var voucherYear = dto.Date.Year;
            var nextNumber = await GetNextVoucherNumberAsync(voucherYear, cancellationToken);

            voucher = new ReceiptVoucher
            {
                VoucherNumber = nextNumber,
                VoucherYear = voucherYear,
                LibraryId = dto.LibraryId,
                SemesterId = dto.SemesterId,
                Amount = dto.Amount,
                PaymentMethod = dto.PaymentMethod,
                ChequeNumber = dto.ChequeNumber,
                BankName = dto.BankName,
                Purpose = dto.Purpose,
                Date = dto.Date,
                CreatedAt = DateTime.UtcNow
            };

            _db.ReceiptVouchers.Add(voucher);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }

        // Reload with includes
        var created = await _db.ReceiptVouchers
            .Include(rv => rv.Library).ThenInclude(l => l.Governorate)
            .Include(rv => rv.Library).ThenInclude(l => l.City)
            .Include(rv => rv.Semester)
            .FirstOrDefaultAsync(rv => rv.Id == voucher.Id, cancellationToken);
        if (created == null)
            throw new InvalidOperationException("Failed to retrieve created receipt voucher");
        return created;
    }

    public async Task<List<ReceiptVoucher>> GetAllAsync(int? libraryId, int? semesterId, DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken = default)
    {
        var activeSemesterIds = await _academicYearHelper.GetActiveSemesterIdsAsync(cancellationToken);
        var query = _db.ReceiptVouchers
            .Include(rv => rv.Library).ThenInclude(l => l.Governorate)
            .Include(rv => rv.Library).ThenInclude(l => l.City)
            .Include(rv => rv.Semester)
            .AsQueryable();

        if (libraryId.HasValue && libraryId.Value > 0)
            query = query.Where(rv => rv.LibraryId == libraryId.Value);

        if (semesterId.HasValue && semesterId.Value > 0)
            query = query.Where(rv => rv.SemesterId == semesterId.Value);
        else if (activeSemesterIds.Count > 0)
            query = query.Where(rv => rv.SemesterId == null || activeSemesterIds.Contains(rv.SemesterId.Value));

        if (fromDate.HasValue)
            query = query.Where(rv => rv.Date >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(rv => rv.Date <= toDate.Value);

        return await query.OrderByDescending(rv => rv.Date).ToListAsync(cancellationToken);
    }

    public async Task<ReceiptVoucher?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _db.ReceiptVouchers
            .Include(rv => rv.Library).ThenInclude(l => l.Governorate)
            .Include(rv => rv.Library).ThenInclude(l => l.City)
            .Include(rv => rv.Semester)
            .FirstOrDefaultAsync(rv => rv.Id == id, cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var voucher = await _db.ReceiptVouchers.FindAsync([id], cancellationToken)
            ?? throw new InvalidOperationException("سند القبض غير موجود");

        var hasClearance = voucher.SemesterId == null
            ? await _db.Invoices.AnyAsync(i =>
                i.LibraryId == voucher.LibraryId &&
                i.Type == "clearance", cancellationToken)
            : await _db.Invoices.AnyAsync(i =>
                i.LibraryId == voucher.LibraryId &&
                i.SemesterId == voucher.SemesterId &&
                i.Type == "clearance", cancellationToken);

        if (hasClearance)
            throw new InvalidOperationException("لا يمكن حذف سند القبض بعد إنشاء المخالصة");

        _db.ReceiptVouchers.Remove(voucher);
        await _db.SaveChangesAsync(cancellationToken);
    }

    internal static ReceiptVoucherDto ToDto(ReceiptVoucher rv)
    {
        return new ReceiptVoucherDto
        {
            Id = rv.Id,
            VoucherNumber = rv.VoucherNumber,
            VoucherYear = rv.VoucherYear,
            DisplayNumber = rv.DisplayNumber,
            LibraryId = rv.LibraryId,
            LibraryName = rv.Library?.Name ?? "",
            GovernorateName = rv.Library?.Governorate?.Name ?? "",
            CityName = rv.Library?.City?.Name ?? "",
            SemesterId = rv.SemesterId,
            SemesterName = rv.Semester?.Name,
            Amount = rv.Amount,
            PaymentMethod = rv.PaymentMethod,
            ChequeNumber = rv.ChequeNumber,
            BankName = rv.BankName,
            Purpose = rv.Purpose,
            Date = rv.Date,
            CreatedAt = rv.CreatedAt
        };
    }
}
