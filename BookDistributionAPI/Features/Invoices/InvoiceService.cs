using System.Collections.Concurrent;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using BookDistributionAPI.Data;
using BookDistributionAPI.Features.Books;
using BookDistributionAPI.Features.Libraries;
using BookDistributionAPI.Features.Semesters;

namespace BookDistributionAPI.Features.Invoices;

public class InvoiceBusinessService
{
    private const string OrderType = "order";
    private const string RefundType = "refund";
    private const string ClearanceType = "clearance";
    private const string PendingPrintStatus = "pending";
    private const int LockCleanupInterval = 50;

    private readonly AppDbContext _db;
    // In-memory lock per semester for sequential invoice numbering.
    // Safe for single-instance deployment (one Docker container).
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private static int _lockAccessCount;

    public InvoiceBusinessService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Gets the next sequential invoice number for a given library, year.
    /// All invoice types share the same sequence per (library, semester).
    /// </summary>
    public async Task<int> GetNextInvoiceNumberAsync(int libraryId, int semesterId, int invoiceYear, CancellationToken cancellationToken = default)
    {
        var maxNumber = await _db.Invoices
            .IgnoreQueryFilters()
            .Where(i => i.LibraryId == libraryId
                     && i.SemesterId == semesterId
                     && i.InvoiceYear == invoiceYear)
            .MaxAsync(i => (int?)i.InvoiceNumber, cancellationToken) ?? 0;
        return maxNumber + 1;
    }

    /// <summary>
    /// Formats the display number: e.g. 2026 + "A" + 1 → "2026A1"
    /// </summary>
    public static string FormatDisplayNumber(int invoiceYear, string termCode, int invoiceNumber)
    {
        return $"{invoiceYear}{termCode}{invoiceNumber}";
    }

    public async Task<Invoice> CreateOrderAsync(CreateOrderDto dto, CancellationToken cancellationToken = default)
    {
        var requestedItems = NormalizeItems(dto.Items);

        await using var transaction = await BeginInvoiceTransactionAsync(dto.SemesterId, cancellationToken);

        var semester = await GetSemesterAsync(dto.SemesterId, cancellationToken);
        var library = await GetActiveLibraryAsync(dto.LibraryId, cancellationToken);
        await EnsureNoClearanceAsync(dto.LibraryId, dto.SemesterId, cancellationToken);

        var books = await LoadBooksAsync(requestedItems.Keys, dto.SemesterId, cancellationToken);
        var invoiceItems = new List<InvoiceItem>();
        decimal totalAmount = 0;

        foreach (var (bookId, quantity) in requestedItems)
        {
            if (!books.TryGetValue(bookId, out var book))
                throw new InvalidOperationException($"الكتاب برقم {bookId} غير موجود");

            if (book.StockQuantity < quantity)
                throw new InvalidOperationException($"المخزون غير كافٍ للكتاب: {book.Name} (المتوفر: {book.StockQuantity})");

            book.StockQuantity -= quantity;

            var lineTotal = quantity * book.Price;
            totalAmount += lineTotal;

            invoiceItems.Add(CreateItemSnapshot(book, quantity, book.Price, lineTotal));
        }

        var invoice = await CreateInvoiceAsync(
            semester,
            library,
            OrderType,
            totalAmount,
            invoiceItems,
            cancellationToken);

        await transaction.CommitAsync();
        return invoice;
    }

    public async Task<Invoice> CreateRefundAsync(CreateRefundDto dto, CancellationToken cancellationToken = default)
    {
        var requestedItems = NormalizeItems(dto.Items);

        await using var transaction = await BeginInvoiceTransactionAsync(dto.SemesterId, cancellationToken);

        var semester = await GetSemesterAsync(dto.SemesterId, cancellationToken);
        var library = await GetActiveLibraryAsync(dto.LibraryId, cancellationToken);
        await EnsureNoClearanceAsync(dto.LibraryId, dto.SemesterId, cancellationToken);

        var books = await LoadBooksAsync(requestedItems.Keys, dto.SemesterId, cancellationToken);
        var refundableQuantities = await GetRefundableQuantitiesAsync(dto.LibraryId, dto.SemesterId, cancellationToken);
        
        // Get original sale prices via FIFO for accurate refund pricing
        var originalPrices = await GetOriginalSalePricesAsync(dto.LibraryId, dto.SemesterId, cancellationToken);
        
        var invoiceItems = new List<InvoiceItem>();
        decimal totalAmount = 0;

        foreach (var (bookId, quantity) in requestedItems)
        {
            if (!books.TryGetValue(bookId, out var book))
                throw new InvalidOperationException($"الكتاب برقم {bookId} غير موجود");
            refundableQuantities.TryGetValue(bookId, out var availableToRefund);

            if (quantity > availableToRefund)
                throw new InvalidOperationException($"لا يمكن استرجاع كمية أكبر من المشتراة ({availableToRefund}) للكتاب: {book.Name}");

            book.StockQuantity += quantity;

            // Use original sale price (FIFO) instead of current book price
            var unitPrice = originalPrices.TryGetValue(bookId, out var origPrice) ? origPrice : book.Price;
            var lineTotal = quantity * unitPrice;
            totalAmount += lineTotal;

            invoiceItems.Add(CreateItemSnapshot(book, quantity, unitPrice, lineTotal));
        }

        var invoice = await CreateInvoiceAsync(
            semester,
            library,
            RefundType,
            totalAmount,
            invoiceItems,
            cancellationToken);

        await transaction.CommitAsync();
        return invoice;
    }



    public async Task<Invoice> CreateClearanceAsync(int libraryId, int semesterId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginInvoiceTransactionAsync(semesterId, cancellationToken);

        var semester = await GetSemesterAsync(semesterId, cancellationToken);
        var library = await GetLibraryForClearanceAsync(libraryId, cancellationToken);
        await EnsureNoClearanceAsync(libraryId, semesterId, cancellationToken);
        var settlement = await EnsureFullyPaidAsync(libraryId, semesterId, cancellationToken);

        var libraryInvoices = await _db.Invoices
            .Include(i => i.Items)
            .Where(i => i.LibraryId == libraryId && i.SemesterId == semesterId && i.Type != ClearanceType)
            .ToListAsync(cancellationToken);

        var clearanceResults = BuildClearanceItems(libraryInvoices, out var totalAmount);

        if (clearanceResults.Count == 0)
            throw new InvalidOperationException("لا توجد عمليات لإنشاء مخالصة لهذه المكتبة");

        var invoiceItems = clearanceResults.Select(r => new InvoiceItem
        {
            BookId = r.Item.BookId,
            BookName = r.Item.BookName,
            BookGrade = r.Item.BookGrade,
            Quantity = r.Item.Quantity,
            UnitPrice = r.Item.UnitPrice,
            Total = r.Item.Total
        }).ToList();

        var invoice = await CreateInvoiceAsync(
            semester,
            library,
            ClearanceType,
            totalAmount,
            invoiceItems,
            cancellationToken,
            settlement.PaidAmount,
            settlement.OutstandingAmount);

        await transaction.CommitAsync();
        return invoice;
    }

    public Task<bool> HasRefundAsync(int libraryId, int semesterId, CancellationToken cancellationToken = default)
    {
        return _db.Invoices.AnyAsync(invoice =>
            invoice.LibraryId == libraryId &&
            invoice.SemesterId == semesterId &&
            invoice.Type == RefundType,
            cancellationToken);
    }

    public async Task<ClearancePreviewDto> GetClearancePreviewAsync(int? libraryId, int semesterId, CancellationToken cancellationToken = default)
    {
        var semester = await GetSemesterAsync(semesterId, cancellationToken);

        var activeLibIds = await _db.Libraries
            .Where(l => l.IsActive)
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);

        var query = _db.Invoices
            .Include(i => i.Items)
            .Where(i => i.SemesterId == semesterId && i.Type != ClearanceType && activeLibIds.Contains(i.LibraryId));

        if (libraryId.HasValue && libraryId.Value > 0)
        {
            query = query.Where(i => i.LibraryId == libraryId.Value);
        }

        var libraryInvoices = await query.ToListAsync(cancellationToken);
        var clearanceResults = BuildClearanceItems(libraryInvoices, out var totalAmount);

        // Get total paid via receipt vouchers for this library/semester
        decimal paidAmount = 0;
        if (libraryId.HasValue && libraryId.Value > 0)
        {
            paidAmount = await _db.ReceiptVouchers
                .Where(rv => rv.LibraryId == libraryId.Value && rv.SemesterId == semesterId)
                .SumAsync(rv => (decimal?)rv.Amount, cancellationToken) ?? 0;
        }
        else
        {
            var libraryIds = libraryInvoices.Select(i => i.LibraryId).Distinct().ToList();
            paidAmount = await _db.ReceiptVouchers
                .Where(rv => rv.SemesterId == semesterId && libraryIds.Contains(rv.LibraryId))
                .SumAsync(rv => (decimal?)rv.Amount, cancellationToken) ?? 0;
        }

        var dto = new ClearancePreviewDto
        {
            SemesterId = semester.Id,
            SemesterName = semester.Name,
            TermCode = semester.Code,
            TotalAmount = totalAmount,
            PaidAmount = paidAmount,
            Items = clearanceResults.Select(r => new InvoiceItemDto
            {
                Id = r.Item.Id,
                BookId = r.Item.BookId,
                BookName = r.Item.BookName,
                BookGrade = r.Item.BookGrade,
                Quantity = r.Item.Quantity,
                UnitPrice = r.Item.UnitPrice,
                Total = r.Item.Total,
                OrderedQty = r.OrderedQty,
                RefundedQty = r.RefundedQty,
                SoldQuantity = r.Item.Quantity,
                AmountDue = r.Item.Total
            }).ToList()
        };

        if (libraryId.HasValue && libraryId.Value > 0)
        {
            var library = await _db.Libraries
                .Include(l => l.Governorate)
                .Include(l => l.City)
                .FirstOrDefaultAsync(l => l.Id == libraryId.Value, cancellationToken)
                ?? throw new InvalidOperationException("المكتبة غير موجودة");

            dto.LibraryId = library.Id;
            dto.LibraryName = library.Name;
            dto.GovernorateName = library.Governorate?.Name ?? "";
            dto.CityName = library.City?.Name ?? "";
            dto.ResponsibleName = library.ResponsibleName;
            dto.ResponsiblePhone = library.ResponsiblePhone;
        }
        else
        {
            dto.LibraryName = "جميع المكتبات";
        }

        return dto;
    }

    internal static InvoiceDto ToDto(Invoice inv)
    {
        return new InvoiceDto
        {
            Id = inv.Id,
            InvoiceNumber = inv.InvoiceNumber,
            InvoiceYear = inv.InvoiceYear,
            TermCode = inv.TermCode,
            DisplayNumber = inv.DisplayNumber,
            Type = inv.Type,
            LibraryId = inv.LibraryId,
            LibraryName = inv.LibraryName,
            GovernorateName = inv.Library?.Governorate?.Name ?? "",
            CityName = inv.Library?.City?.Name ?? "",
            SemesterId = inv.SemesterId,
            SemesterName = inv.Semester?.Name ?? "",
            Date = inv.Date,
            TotalAmount = inv.TotalAmount,
            ClearancePaidAmount = inv.ClearancePaidAmount,
            ClearanceOutstandingAmount = inv.ClearanceOutstandingAmount,
            PrintStatus = inv.PrintStatus,
            ResponsibleName = inv.ResponsibleName,
            ResponsiblePhone = inv.ResponsiblePhone,
            Items = inv.Items.Select(ii => new InvoiceItemDto
            {
                Id = ii.Id,
                BookId = ii.BookId,
                BookName = ii.BookName,
                BookGrade = ii.BookGrade,
                Quantity = ii.Quantity,
                UnitPrice = ii.UnitPrice,
                Total = ii.Total
            }).ToList()
        };
    }

    public async Task DeleteInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

        if (invoice == null)
            throw new InvalidOperationException("الفاتورة غير موجودة");

        await using var transaction = await BeginInvoiceTransactionAsync(invoice.SemesterId, cancellationToken);
        await DeleteInvoiceInternalAsync(invoice, saveChanges: true, cancellationToken);
        await transaction.CommitAsync();
    }

    private async Task DeleteInvoiceInternalAsync(Invoice invoice, bool saveChanges, CancellationToken cancellationToken)
    {
        if (invoice.Type != ClearanceType)
        {
            await EnsureNoClearanceAsync(invoice.LibraryId, invoice.SemesterId, cancellationToken);
        }

        if (invoice.Type == OrderType)
        {
            var refundableQuantities = await GetRefundableQuantitiesAsync(invoice.LibraryId, invoice.SemesterId, cancellationToken);
            foreach (var item in invoice.Items)
            {
                refundableQuantities.TryGetValue(item.BookId, out var available);
                if (available < item.Quantity)
                    throw new InvalidOperationException($"لا يمكن حذف فاتورة الطلب: الكمية المتاحة للتراجع ({available}) أقل من كمية الفاتورة ({item.Quantity}) للكتاب «{item.BookName}» بسبب وجود مرتجعات مسجلة. يرجى حذف المرتجعات المتعلقة أولاً.");
            }
        }

        Dictionary<int, Book>? booksForRefund = null;
        if (invoice.Type == RefundType)
        {
            var bookIds = invoice.Items.Select(i => i.BookId).ToList();
            booksForRefund = await _db.Books
                .Where(b => bookIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, cancellationToken);

            foreach (var item in invoice.Items)
            {
                if (booksForRefund.TryGetValue(item.BookId, out var book) && book.StockQuantity < item.Quantity)
                    throw new InvalidOperationException($"لا يمكن حذف المرتجع: المخزون الحالي للكتاب «{book.Name}» ({book.StockQuantity}) أقل من كمية المرتجع ({item.Quantity})");
            }
        }

        // Batch stock restoration in-memory, then single SaveChangesAsync
        var affectedBookIds = invoice.Items.Select(i => i.BookId).ToList();
        var booksToUpdate = await _db.Books
            .Where(b => affectedBookIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        foreach (var item in invoice.Items)
        {
            if (!booksToUpdate.TryGetValue(item.BookId, out var book))
                continue;

            if (invoice.Type == OrderType)
                book.StockQuantity += item.Quantity;
            else if (invoice.Type == RefundType)
                book.StockQuantity -= item.Quantity;
        }

        invoice.IsActive = false;
        if (saveChanges)
            await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteInvoicesAsync(List<int> ids, CancellationToken cancellationToken = default)
    {
        var requestedIds = ids.Distinct().ToList();
        var invoices = await _db.Invoices
            .Include(i => i.Items)
            .Where(i => requestedIds.Contains(i.Id))
            .ToListAsync(cancellationToken);

        if (invoices.Count != requestedIds.Count)
            throw new InvalidOperationException("بعض الفواتير غير موجودة");

        var semesterIds = invoices.Select(i => i.SemesterId).Distinct().ToList();
        await using var transaction = await BeginInvoiceTransactionAsync(semesterIds, cancellationToken);

        var orderedInvoices = invoices
            .OrderBy(i => i.Type == "clearance" ? 0 : i.Type == "refund" ? 1 : 2)
            .ThenByDescending(i => i.Date)
            .ToList();

        foreach (var typeGroup in orderedInvoices.GroupBy(i => i.Type))
        {
            foreach (var invoice in typeGroup)
                await DeleteInvoiceInternalAsync(invoice, saveChanges: false, cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync();
    }

    public async Task RestoreInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _db.Invoices
            .IgnoreQueryFilters()
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && !i.IsActive, cancellationToken);

        if (invoice == null)
            throw new InvalidOperationException("الفاتورة غير موجودة أو هي نشطة بالفعل");

        await using var transaction = await BeginInvoiceTransactionAsync(invoice.SemesterId, cancellationToken);
        await RestoreInvoiceInternalAsync(invoice, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync();
    }

    public async Task RestoreInvoicesAsync(List<int> ids, CancellationToken cancellationToken = default)
    {
        var requestedIds = ids.Distinct().ToList();
        var invoices = await _db.Invoices
            .IgnoreQueryFilters()
            .Include(i => i.Items)
            .Where(i => requestedIds.Contains(i.Id))
            .ToListAsync(cancellationToken);

        if (invoices.Count != requestedIds.Count)
            throw new InvalidOperationException("بعض الفواتير غير موجودة");

        if (invoices.Any(i => i.IsActive))
            throw new InvalidOperationException("بعض الفواتير نشطة بالفعل ولا يمكن استعادتها");

        await using var transaction = await BeginInvoiceTransactionAsync(
            invoices.Select(i => i.SemesterId), cancellationToken);

        var orderedInvoices = invoices
            .OrderBy(i => i.Type == OrderType ? 0 : i.Type == RefundType ? 1 : 2)
            .ThenBy(i => i.Date)
            .ToList();

        foreach (var typeGroup in orderedInvoices.GroupBy(i => i.Type))
        {
            foreach (var invoice in typeGroup)
                await RestoreInvoiceInternalAsync(invoice, cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync();
    }

    private async Task RestoreInvoiceInternalAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        // Prevent restoring if a clearance was created after deletion
        if (invoice.Type != "clearance")
        {
            var hasClearance = await _db.Invoices.AnyAsync(i =>
                i.LibraryId == invoice.LibraryId &&
                i.SemesterId == invoice.SemesterId &&
                i.Type == "clearance", cancellationToken);
            if (hasClearance)
                throw new InvalidOperationException("لا يمكن استعادة الفاتورة بعد إنشاء المخالصة النهائية");
        }

        // Restore stock quantities
        var affectedBookIds = invoice.Items.Select(i => i.BookId).ToList();
        var booksToUpdate = await _db.Books
            .Where(b => affectedBookIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        // Validation loop
        if (invoice.Type == "order")
        {
            foreach (var item in invoice.Items)
            {
                if (booksToUpdate.TryGetValue(item.BookId, out var book))
                {
                    if (book.StockQuantity < item.Quantity)
                        throw new InvalidOperationException($"لا يمكن استعادة الفاتورة: المخزون الحالي للكتاب «{book.Name}» ({book.StockQuantity}) أقل من الكمية المطلوبة ({item.Quantity}). قد يكون المخزون قد استُهلك منذ حذف الفاتورة.");
                }
            }
        }
        else if (invoice.Type == "refund")
        {
            var refundableQuantities = await GetRefundableQuantitiesAsync(invoice.LibraryId, invoice.SemesterId, cancellationToken);
            foreach (var item in invoice.Items)
            {
                refundableQuantities.TryGetValue(item.BookId, out var available);
                if (item.Quantity > available)
                    throw new InvalidOperationException($"لا يمكن استعادة المرتجع: كمية المرتجع ({item.Quantity}) تتجاوز الكمية المتاحة للاسترجاع ({available}) للكتاب «{item.BookName}»");
            }
        }

        foreach (var item in invoice.Items)
        {
            if (!booksToUpdate.TryGetValue(item.BookId, out var book))
                continue;

            if (invoice.Type == "order")
            {
                book.StockQuantity -= item.Quantity;
            }
            else if (invoice.Type == "refund")
            {
                book.StockQuantity += item.Quantity;
            }
        }

        invoice.IsActive = true;
    }

    private Task<TransactionWithLock> BeginInvoiceTransactionAsync(int semesterId, CancellationToken cancellationToken = default)
    {
        return BeginInvoiceTransactionAsync(new[] { semesterId }, cancellationToken);
    }

    private async Task<TransactionWithLock> BeginInvoiceTransactionAsync(IEnumerable<int> semesterIds, CancellationToken cancellationToken = default)
    {
        var semaphores = semesterIds
            .Distinct()
            .OrderBy(id => id)
            .Select(semesterId => _locks.GetOrAdd($"invoice-semester:{semesterId}", _ => new SemaphoreSlim(1, 1)))
            .ToList();

        foreach (var semaphore in semaphores)
            await semaphore.WaitAsync(cancellationToken);

        if (Interlocked.Increment(ref _lockAccessCount) % LockCleanupInterval == 0)
            CleanupStaleLocks();

        try
        {
            var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
            return new TransactionWithLock(transaction, semaphores);
        }
        catch
        {
            foreach (var semaphore in semaphores.AsEnumerable().Reverse())
                semaphore.Release();
            throw;
        }
    }

    private static void CleanupStaleLocks()
    {
        foreach (var kvp in _locks)
        {
            // CurrentCount == 1 means semaphore is available (not acquired).
            // Safe to remove; will be re-created on next use.
            if (kvp.Value.CurrentCount == 1)
                _locks.TryRemove(kvp.Key, out _);
        }
    }

    private sealed class TransactionWithLock : IAsyncDisposable
    {
        private readonly IDbContextTransaction _transaction;
        private readonly IReadOnlyList<SemaphoreSlim> _semaphores;
        private bool _disposed;

        public TransactionWithLock(IDbContextTransaction transaction, IReadOnlyList<SemaphoreSlim> semaphores)
        {
            _transaction = transaction;
            _semaphores = semaphores;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                await _transaction.DisposeAsync();
            }
            finally
            {
                foreach (var semaphore in _semaphores.Reverse())
                    semaphore.Release();
            }
        }

        public async Task CommitAsync(CancellationToken ct = default)
        {
            await _transaction.CommitAsync(ct);
        }

        public async Task RollbackAsync(CancellationToken ct = default)
        {
            await _transaction.RollbackAsync(ct);
        }
    }

    private async Task<Semester> GetSemesterAsync(int semesterId, CancellationToken cancellationToken = default)
    {
        var semester = await _db.Semesters
            .Include(s => s.AcademicYear)
            .FirstOrDefaultAsync(s => s.Id == semesterId, cancellationToken)
            ?? throw new InvalidOperationException("الفصل الدراسي غير موجود");

        if (!semester.IsActive || !semester.AcademicYear.IsActive)
            throw new InvalidOperationException("لا يمكن إنشاء عمليات على فصل دراسي غير نشط");

        return semester;
    }

    private async Task<Library> GetActiveLibraryAsync(int libraryId, CancellationToken cancellationToken = default)
    {
        var library = await _db.Libraries.FirstOrDefaultAsync(l => l.Id == libraryId, cancellationToken)
            ?? throw new InvalidOperationException("المكتبة غير موجودة");

        if (!library.IsActive)
            throw new InvalidOperationException("لا يمكن إنشاء فاتورة لمكتبة غير نشطة");

        return library;
    }

    private async Task<Library> GetLibraryForClearanceAsync(int libraryId, CancellationToken cancellationToken = default)
    {
        var library = await _db.Libraries.FirstOrDefaultAsync(l => l.Id == libraryId, cancellationToken)
            ?? throw new InvalidOperationException("المكتبة غير موجودة");

        if (!library.IsActive)
            throw new InvalidOperationException("لا يمكن إنشاء مخالصة لمكتبة محذوفة");

        return library;
    }

    private async Task EnsureNoClearanceAsync(int libraryId, int semesterId, CancellationToken cancellationToken = default)
    {
        var hasClearance = await _db.Invoices.AnyAsync(i =>
            i.LibraryId == libraryId &&
            i.SemesterId == semesterId &&
            i.Type == ClearanceType, cancellationToken);

        if (hasClearance)
            throw new InvalidOperationException("لا يمكن إنشاء طلب أو مرتجع بعد إصدار المخالصة النهائية");
    }

    private async Task<SettlementTotals> EnsureFullyPaidAsync(int libraryId, int semesterId, CancellationToken cancellationToken)
    {
        var invoiceTotals = await _db.Invoices
            .Where(invoice => invoice.LibraryId == libraryId && invoice.SemesterId == semesterId)
            .GroupBy(invoice => invoice.Type)
            .Select(group => new { Type = group.Key, Total = group.Sum(invoice => invoice.TotalAmount) })
            .ToListAsync(cancellationToken);

        var orderTotal = invoiceTotals.FirstOrDefault(total => total.Type == OrderType)?.Total ?? 0m;
        var refundTotal = invoiceTotals.FirstOrDefault(total => total.Type == RefundType)?.Total ?? 0m;
        var paidTotal = await _db.ReceiptVouchers
            .Where(voucher => voucher.LibraryId == libraryId && voucher.SemesterId == semesterId)
            .SumAsync(voucher => (decimal?)voucher.Amount, cancellationToken) ?? 0m;

        var outstandingAmount = orderTotal - refundTotal - paidTotal;
        if (outstandingAmount > 0m)
            throw new InvalidOperationException(
                $"لا يمكن إصدار المخالصة النهائية قبل سداد الرصيد المتبقي ({outstandingAmount:N3})");

        return new SettlementTotals(paidTotal, Math.Max(0m, outstandingAmount));
    }

    private sealed record SettlementTotals(decimal PaidAmount, decimal OutstandingAmount);

    private async Task<Dictionary<int, Book>> LoadBooksAsync(IEnumerable<int> bookIds, int semesterId, CancellationToken cancellationToken = default)
    {
        var ids = bookIds.ToList();
        var books = await _db.Books
            .Where(b => ids.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        var missingIds = ids.Except(books.Keys).ToList();
        if (missingIds.Count > 0)
            throw new InvalidOperationException($"كتب غير موجودة: {string.Join(", ", missingIds)}");

        var wrongSemesterBooks = books.Values
            .Where(b => b.SemesterId != semesterId)
            .Select(b => b.Name)
            .ToList();

        if (wrongSemesterBooks.Count > 0)
            throw new InvalidOperationException($"كتب لا تتبع الفصل الدراسي المحدد: {string.Join(", ", wrongSemesterBooks)}");

        return books;
    }

    private async Task<Dictionary<int, int>> GetRefundableQuantitiesAsync(int libraryId, int semesterId, CancellationToken cancellationToken = default)
    {
        var orderQtys = await _db.Invoices
            .Where(i => i.LibraryId == libraryId && i.SemesterId == semesterId && i.Type == OrderType)
            .SelectMany(i => i.Items)
            .GroupBy(item => item.BookId)
            .Select(g => new { BookId = g.Key, Qty = g.Sum(item => item.Quantity) })
            .ToDictionaryAsync(x => x.BookId, x => x.Qty, cancellationToken);

        var refundQtys = await _db.Invoices
            .Where(i => i.LibraryId == libraryId && i.SemesterId == semesterId && i.Type == RefundType)
            .SelectMany(i => i.Items)
            .GroupBy(item => item.BookId)
            .Select(g => new { BookId = g.Key, Qty = g.Sum(item => item.Quantity) })
            .ToDictionaryAsync(x => x.BookId, x => x.Qty, cancellationToken);

        var result = new Dictionary<int, int>();
        foreach (var (bookId, qty) in orderQtys)
            result[bookId] = qty - refundQtys.GetValueOrDefault(bookId, 0);
        foreach (var (bookId, qty) in refundQtys)
            result.TryAdd(bookId, -qty);
        return result;
    }

    /// <summary>
    /// Gets original sale prices using FIFO from order invoices.
    /// Returns the first (earliest) sale price for each book.
    /// Uses Invoice.Id as tiebreaker for same-date invoices.
    /// </summary>
    private async Task<Dictionary<int, decimal>> GetOriginalSalePricesAsync(int libraryId, int semesterId, CancellationToken cancellationToken = default)
    {
        var priceData = await _db.InvoiceItems
            .Where(ii => ii.Invoice.LibraryId == libraryId && ii.Invoice.SemesterId == semesterId && ii.Invoice.Type == OrderType)
            .Select(ii => new { ii.BookId, ii.UnitPrice, ii.Invoice.Date, ii.InvoiceId })
            .ToListAsync(cancellationToken);

        var prices = priceData
            .GroupBy(x => x.BookId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Date).ThenBy(x => x.InvoiceId).Select(x => x.UnitPrice).First());
        return prices;
    }

    private async Task<Invoice> CreateInvoiceAsync(
        Semester semester,
        Library library,
        string type,
        decimal totalAmount,
        List<InvoiceItem> invoiceItems,
        CancellationToken cancellationToken = default,
        decimal clearancePaidAmount = 0m,
        decimal clearanceOutstandingAmount = 0m)
    {
        if (semester.AcademicYear == null)
            throw new InvalidOperationException($"Academic year not loaded for semester {semester.Id}");
        var yearPart = semester.AcademicYear.Name.Split('-')[0];
        if (!int.TryParse(yearPart, out var invoiceYear))
            throw new InvalidOperationException($"Invalid academic year format: {semester.AcademicYear.Name}");
        
        var termCode = semester.Code;
        
        var nextNumber = await GetNextInvoiceNumberAsync(library.Id, semester.Id, invoiceYear, cancellationToken);
        var invoice = new Invoice
        {
            InvoiceNumber = nextNumber,
            InvoiceYear = invoiceYear,
            TermCode = termCode,
            Type = type,
            LibraryId = library.Id,
            LibraryName = library.Name,
            SemesterId = semester.Id,
            Date = DateTime.UtcNow,
            TotalAmount = totalAmount,
            ClearancePaidAmount = clearancePaidAmount,
            ClearanceOutstandingAmount = clearanceOutstandingAmount,
            PrintStatus = PendingPrintStatus,
            ResponsibleName = library.ResponsibleName,
            ResponsiblePhone = library.ResponsiblePhone,
            Items = invoiceItems,
            IsActive = true
        };

        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync(cancellationToken);
        return invoice;
    }

    private static Dictionary<int, int> NormalizeItems(IEnumerable<CreateInvoiceItemDto>? items)
    {
        if (items == null)
            throw new InvalidOperationException("الرجاء إدخال كميات لبعض المواد على الأقل");

        var normalized = new Dictionary<int, int>();
        foreach (var item in items)
        {
            if (item.BookId <= 0)
                throw new InvalidOperationException("رقم الكتاب غير صحيح");

            if (item.Quantity <= 0)
                throw new InvalidOperationException("الكمية يجب أن تكون أكبر من صفر");

            normalized[item.BookId] = normalized.TryGetValue(item.BookId, out var existing)
                ? checked(existing + item.Quantity)
                : item.Quantity;
        }

        if (normalized.Count == 0)
            throw new InvalidOperationException("الرجاء إدخال كميات لبعض المواد على الأقل");

        return normalized;
    }

    private static InvoiceItem CreateItemSnapshot(Book book, int quantity, decimal unitPrice, decimal lineTotal)
    {
        return new InvoiceItem
        {
            BookId = book.Id,
            BookName = book.Name,
            BookGrade = book.Grade,
            Quantity = quantity,
            UnitPrice = unitPrice,
            Total = lineTotal
        };
    }

    private static List<(InvoiceItem Item, int OrderedQty, int RefundedQty)> BuildClearanceItems(List<Invoice> libraryInvoices, out decimal totalAmount)
    {
        var summaries = new Dictionary<int, ClearanceSummary>();

        foreach (var invoice in libraryInvoices)
        {
            foreach (var item in invoice.Items)
            {
                if (!summaries.TryGetValue(item.BookId, out var summary))
                {
                    summary = new ClearanceSummary(item.BookId, item.BookName, item.BookGrade);
                    summaries[item.BookId] = summary;
                }

                if (invoice.Type == OrderType)
                {
                    summary.OrderedQty += item.Quantity;
                    summary.TotalOrderCost += item.Total;
                }
                else if (invoice.Type == RefundType)
                {
                    summary.RefundedQty += item.Quantity;
                    summary.TotalRefundCost += item.Total;
                }
            }
        }

        totalAmount = 0;
        var items = new List<(InvoiceItem, int, int)>();

        foreach (var summary in summaries.Values)
        {
            var netQty = summary.OrderedQty - summary.RefundedQty;
            if (netQty <= 0)
                continue;

            var lineTotal = summary.TotalOrderCost - summary.TotalRefundCost;
            var avgPrice = decimal.Round(lineTotal / netQty, 3, MidpointRounding.AwayFromZero);
            totalAmount += lineTotal;

            items.Add((new InvoiceItem
            {
                BookId = summary.BookId,
                BookName = summary.Name,
                BookGrade = summary.Grade,
                Quantity = netQty,
                UnitPrice = avgPrice,
                Total = lineTotal
            }, summary.OrderedQty, summary.RefundedQty));
        }

        return items;
    }

    private sealed class ClearanceSummary
    {
        public ClearanceSummary(int bookId, string name, string grade)
        {
            BookId = bookId;
            Name = name;
            Grade = grade;
        }

        public int BookId { get; }
        public string Name { get; }
        public string Grade { get; }
        public decimal TotalOrderCost { get; set; }
        public decimal TotalRefundCost { get; set; }
        public int OrderedQty { get; set; }
        public int RefundedQty { get; set; }
    }
}
