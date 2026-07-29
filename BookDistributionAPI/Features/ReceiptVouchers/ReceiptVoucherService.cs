using System.Collections.Concurrent;
using System.Data;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using BookDistributionAPI.Common;
using BookDistributionAPI.Data;

namespace BookDistributionAPI.Features.ReceiptVouchers;

public class ReceiptVoucherBusinessService
{
    private readonly AppDbContext _db;
    private readonly IAcademicYearHelper _academicYearHelper;
    private const int LockCleanupInterval = 50;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private static int _lockAccessCount;

    public ReceiptVoucherBusinessService(AppDbContext db, IAcademicYearHelper academicYearHelper)
    {
        _db = db;
        _academicYearHelper = academicYearHelper;
    }

    public async Task<int> GetNextVoucherNumberAsync(int voucherYear, CancellationToken cancellationToken = default)
    {
        var maxNumber = await _db.ReceiptVouchers
            .IgnoreQueryFilters()
            .Where(rv => rv.VoucherYear == voucherYear)
            .MaxAsync(rv => (int?)rv.VoucherNumber, cancellationToken) ?? 0;
        return maxNumber + 1;
    }

    public async Task<ReceiptVoucher> CreateAsync(CreateReceiptVoucherDto dto, CancellationToken cancellationToken = default)
    {
        var semesterId = dto.SemesterId
            ?? throw new InvalidOperationException("يجب تحديد الفصل الدراسي لسند القبض");

        var library = await _db.Libraries.FirstOrDefaultAsync(l => l.Id == dto.LibraryId, cancellationToken)
            ?? throw new InvalidOperationException("المكتبة غير موجودة");

        if (!library.IsActive)
            throw new InvalidOperationException("لا يمكن إنشاء سند قبض لمكتبة غير نشطة");

        var semesterExists = await _db.Semesters.AnyAsync(s => s.Id == semesterId, cancellationToken);
        if (!semesterExists)
            throw new InvalidOperationException("الفصل الدراسي غير موجود");

        var hasClearance = await _db.Invoices.AnyAsync(i =>
            i.LibraryId == dto.LibraryId &&
            i.SemesterId == semesterId &&
            i.Type == "clearance", cancellationToken);
        if (hasClearance)
            throw new InvalidOperationException("لا يمكن إنشاء سند قبض بعد إصدار المخالصة النهائية");

        if (dto.PaymentMethod == "cheque" && string.IsNullOrWhiteSpace(dto.ChequeNumber))
            throw new InvalidOperationException("يجب إدخال رقم الشيك عند الدفع بشيك");

        if (dto.Date.Year < 2000 || dto.Date.Year > 2100)
            throw new InvalidOperationException("التاريخ غير صحيح");

        var lockKey = $"voucher-year:{dto.Date.Year}";
        var semaphore = _locks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        if (Interlocked.Increment(ref _lockAccessCount) % LockCleanupInterval == 0)
            CleanupStaleLocks();
        ReceiptVoucher voucher;
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

            await EnsureAmountDoesNotExceedOutstandingAsync(
                dto.LibraryId, semesterId, dto.Amount, cancellationToken);

            var voucherYear = dto.Date.Year;
            var nextNumber = await GetNextVoucherNumberAsync(voucherYear, cancellationToken);

            voucher = new ReceiptVoucher
            {
                VoucherNumber = nextNumber,
                VoucherYear = voucherYear,
                LibraryId = dto.LibraryId,
                LibraryName = library.Name,
                SemesterId = semesterId,
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

    private async Task EnsureAmountDoesNotExceedOutstandingAsync(
        int libraryId,
        int semesterId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var invoiceTotals = await _db.Invoices
            .Where(invoice => invoice.LibraryId == libraryId && invoice.SemesterId == semesterId)
            .GroupBy(invoice => invoice.Type)
            .Select(group => new { Type = group.Key, Total = group.Sum(invoice => invoice.TotalAmount) })
            .ToListAsync(cancellationToken);

        var orderTotal = invoiceTotals.FirstOrDefault(total => total.Type == "order")?.Total ?? 0;
        var refundTotal = invoiceTotals.FirstOrDefault(total => total.Type == "refund")?.Total ?? 0;
        var paidTotal = await _db.ReceiptVouchers
            .Where(voucher => voucher.LibraryId == libraryId && voucher.SemesterId == semesterId)
            .SumAsync(voucher => (decimal?)voucher.Amount, cancellationToken) ?? 0;

        var outstandingAmount = orderTotal - refundTotal - paidTotal;
        if (outstandingAmount <= 0)
            throw new InvalidOperationException("لا يوجد مبلغ مستحق لإصدار سند قبض");

        if (amount > outstandingAmount)
            throw new InvalidOperationException($"مبلغ سند القبض ({amount:N3}) أكبر من الرصيد المستحق ({outstandingAmount:N3})");
    }

    private static void CleanupStaleLocks()
    {
        foreach (var kvp in _locks)
        {
            if (kvp.Value.CurrentCount == 1)
                _locks.TryRemove(kvp.Key, out _);
        }
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
        var voucher = await _db.ReceiptVouchers.FirstOrDefaultAsync(rv => rv.Id == id, cancellationToken)
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

        voucher.IsActive = false;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RestoreAsync(int id, CancellationToken cancellationToken = default)
    {
        var voucher = await _db.ReceiptVouchers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(rv => rv.Id == id && !rv.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("سند القبض غير موجود أو هو نشط بالفعل");

        var hasClearance = voucher.SemesterId == null
            ? await _db.Invoices.AnyAsync(i =>
                i.LibraryId == voucher.LibraryId &&
                i.Type == "clearance", cancellationToken)
            : await _db.Invoices.AnyAsync(i =>
                i.LibraryId == voucher.LibraryId &&
                i.SemesterId == voucher.SemesterId &&
                i.Type == "clearance", cancellationToken);

        if (hasClearance)
            throw new InvalidOperationException("لا يمكن استعادة سند القبض بعد إنشاء المخالصة");

        voucher.IsActive = true;
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
            LibraryName = rv.LibraryName,
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
