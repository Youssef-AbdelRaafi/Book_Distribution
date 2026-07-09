using System.Data;
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

    private readonly AppDbContext _db;

    public InvoiceBusinessService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Gets the current invoice year based on server time.
    /// </summary>
    private static int GetCurrentInvoiceYear() => DateTime.UtcNow.Year;

    /// <summary>
    /// Gets the next sequential invoice number for a given library, year, and term code.
    /// The sequence is per-library, per-year, per-termCode (A/B for orders+refunds, P for clearance).
    /// </summary>
    public async Task<int> GetNextInvoiceNumberAsync(int libraryId, int invoiceYear, string termCode)
    {
        var maxNumber = await _db.Invoices
            .Where(i => i.LibraryId == libraryId 
                     && i.InvoiceYear == invoiceYear 
                     && i.TermCode == termCode)
            .MaxAsync(i => (int?)i.InvoiceNumber) ?? 0;
        return maxNumber + 1;
    }

    /// <summary>
    /// Formats the display number: 2026 → "2026A1", 2027+ → "27A1"
    /// </summary>
    public static string FormatDisplayNumber(int invoiceYear, string termCode, int invoiceNumber)
    {
        var yearPrefix = invoiceYear <= 2026 
            ? invoiceYear.ToString() 
            : (invoiceYear % 100).ToString();
        return $"{yearPrefix}{termCode}{invoiceNumber}";
    }

    public async Task<Invoice> CreateOrderAsync(CreateOrderDto dto)
    {
        var requestedItems = NormalizeItems(dto.Items);

        await using var transaction = await BeginInvoiceTransactionAsync(dto.SemesterId);

        var semester = await GetSemesterAsync(dto.SemesterId);
        var library = await GetActiveLibraryAsync(dto.LibraryId);
        await EnsureNoClearanceAsync(dto.LibraryId, dto.SemesterId);

        var books = await LoadBooksAsync(requestedItems.Keys, dto.SemesterId);
        var invoiceItems = new List<InvoiceItem>();
        decimal totalAmount = 0;

        foreach (var (bookId, quantity) in requestedItems)
        {
            var book = books[bookId];

            if (book.StockQuantity < quantity)
                throw new InvalidOperationException($"المخزون غير كافٍ للكتاب: {book.Name} (المتوفر: {book.StockQuantity})");

            var updatedRows = await _db.Books
                .Where(b => b.Id == bookId && b.StockQuantity >= quantity)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(b => b.StockQuantity, b => b.StockQuantity - quantity));

            if (updatedRows != 1)
                throw new InvalidOperationException($"المخزون غير كافٍ للكتاب: {book.Name}");

            var lineTotal = quantity * book.Price;
            totalAmount += lineTotal;

            invoiceItems.Add(CreateItemSnapshot(book, quantity, book.Price, lineTotal));
        }

        var invoice = await CreateInvoiceAsync(
            semester,
            library,
            OrderType,
            totalAmount,
            invoiceItems);

        await transaction.CommitAsync();
        return invoice;
    }

    public async Task<Invoice> CreateRefundAsync(CreateRefundDto dto)
    {
        var requestedItems = NormalizeItems(dto.Items);

        await using var transaction = await BeginInvoiceTransactionAsync(dto.SemesterId);

        var semester = await GetSemesterAsync(dto.SemesterId);
        var library = await GetActiveLibraryAsync(dto.LibraryId);
        await EnsureNoClearanceAsync(dto.LibraryId, dto.SemesterId);

        var books = await LoadBooksAsync(requestedItems.Keys, dto.SemesterId);
        var refundableQuantities = await GetRefundableQuantitiesAsync(dto.LibraryId, dto.SemesterId);
        
        // Get original sale prices via FIFO for accurate refund pricing
        var originalPrices = await GetOriginalSalePricesAsync(dto.LibraryId, dto.SemesterId);
        
        var invoiceItems = new List<InvoiceItem>();
        decimal totalAmount = 0;

        foreach (var (bookId, quantity) in requestedItems)
        {
            var book = books[bookId];
            refundableQuantities.TryGetValue(bookId, out var availableToRefund);

            if (quantity > availableToRefund)
                throw new InvalidOperationException($"لا يمكن استرجاع كمية أكبر من المشتراة ({availableToRefund}) للكتاب: {book.Name}");

            await _db.Books
                .Where(b => b.Id == bookId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(b => b.StockQuantity, b => b.StockQuantity + quantity));

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
            invoiceItems);

        await transaction.CommitAsync();
        return invoice;
    }

    public async Task<Invoice> CreateClearanceAsync(CreateClearanceDto dto)
    {
        await using var transaction = await BeginInvoiceTransactionAsync(dto.SemesterId);

        var semester = await GetSemesterAsync(dto.SemesterId);
        var library = await GetLibraryForClearanceAsync(dto.LibraryId);

        if (await _db.Invoices.AnyAsync(i =>
            i.LibraryId == dto.LibraryId &&
            i.SemesterId == dto.SemesterId &&
            i.Type == ClearanceType))
            throw new InvalidOperationException("تم إنشاء مخالصة لهذه المكتبة في هذا الفصل بالفعل");

        var libraryInvoices = await _db.Invoices
            .Include(i => i.Items)
            .Where(i => i.LibraryId == dto.LibraryId
                     && i.SemesterId == dto.SemesterId
                     && i.Type != ClearanceType)
            .ToListAsync();

        if (libraryInvoices.Count == 0)
            throw new InvalidOperationException("لا توجد فواتير لإنشاء مخالصة لهذه المكتبة");

        var invoiceItems = BuildClearanceItems(libraryInvoices, out var totalAmount);

        if (invoiceItems.Count == 0 || totalAmount <= 0)
            throw new InvalidOperationException("لا توجد مبالغ مستحقة لإنشاء مخالصة. قد تكون جميع الطلبات مرتجعة بالكامل");

        var invoice = await CreateInvoiceAsync(
            semester,
            library,
            ClearanceType,
            totalAmount,
            invoiceItems);

        await transaction.CommitAsync();
        return invoice;
    }

    public async Task<ClearanceBatchResultDto> CreateBatchClearancesAsync(int semesterId)
    {
        await using var transaction = await BeginInvoiceTransactionAsync(semesterId);

        var semester = await GetSemesterAsync(semesterId);
        var libraryIds = await _db.Invoices
            .Where(i => i.SemesterId == semesterId && i.Type != ClearanceType)
            .Select(i => i.LibraryId)
            .Distinct()
            .ToListAsync();

        var created = new List<Invoice>();
        decimal batchTotal = 0;

        foreach (var libraryId in libraryIds)
        {
            if (await _db.Invoices.AnyAsync(i =>
                i.LibraryId == libraryId &&
                i.SemesterId == semesterId &&
                i.Type == ClearanceType))
                continue;

            var libraryInvoices = await _db.Invoices
                .Include(i => i.Items)
                .Where(i => i.LibraryId == libraryId
                         && i.SemesterId == semesterId
                         && i.Type != ClearanceType)
                .ToListAsync();

            var invoiceItems = BuildClearanceItems(libraryInvoices, out var totalAmount);
            if (invoiceItems.Count == 0 || totalAmount <= 0)
                continue;

            var library = await GetLibraryForClearanceAsync(libraryId);
            var invoice = await CreateInvoiceAsync(
                semester,
                library,
                ClearanceType,
                totalAmount,
                invoiceItems);

            created.Add(invoice);
            batchTotal += totalAmount;
        }

        if (created.Count == 0)
            throw new InvalidOperationException("لا توجد مخالصات مستحقة للإنشاء");

        await transaction.CommitAsync();

        // Reload each clearance individually with its own print data
        var loadedInvoices = new List<InvoiceDto>();
        foreach (var c in created)
        {
            var loaded = await _db.Invoices
                .Include(i => i.Items)
                .Include(i => i.Library).ThenInclude(l => l.Governorate)
                .Include(i => i.Library).ThenInclude(l => l.City)
                .Include(i => i.Semester)
                .FirstOrDefaultAsync(i => i.Id == c.Id);

            if (loaded != null)
                loadedInvoices.Add(ToDto(loaded));
        }

        return new ClearanceBatchResultDto
        {
            Count = loadedInvoices.Count,
            TotalAmount = batchTotal,
            Invoices = loadedInvoices
        };
    }

    public async Task<ClearancePreviewDto> GetClearancePreviewAsync(int? libraryId, int semesterId)
    {
        var semester = await GetSemesterAsync(semesterId);

        var query = _db.Invoices
            .Include(i => i.Items)
            .Where(i => i.SemesterId == semesterId && i.Type != ClearanceType);

        if (libraryId.HasValue && libraryId.Value > 0)
        {
            query = query.Where(i => i.LibraryId == libraryId.Value);
        }

        var libraryInvoices = await query.ToListAsync();
        var invoiceItems = BuildClearanceItems(libraryInvoices, out var totalAmount);

        var dto = new ClearancePreviewDto
        {
            SemesterId = semester.Id,
            SemesterName = semester.Name,
            TermCode = semester.Code,
            TotalAmount = totalAmount,
            Items = invoiceItems.Select(ii => new InvoiceItemDto
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

        if (libraryId.HasValue && libraryId.Value > 0)
        {
            var library = await _db.Libraries
                .Include(l => l.Governorate)
                .Include(l => l.City)
                .FirstOrDefaultAsync(l => l.Id == libraryId.Value)
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

    public static InvoiceDto ToDto(Invoice inv)
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
            LibraryName = inv.Library?.Name ?? "",
            GovernorateName = inv.Library?.Governorate?.Name ?? "",
            CityName = inv.Library?.City?.Name ?? "",
            SemesterId = inv.SemesterId,
            SemesterName = inv.Semester?.Name ?? "",
            Date = inv.Date,
            TotalAmount = inv.TotalAmount,
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

    public async Task DeleteInvoiceAsync(int invoiceId)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null)
            throw new InvalidOperationException("الفاتورة غير موجودة");

        if (invoice.Type == ClearanceType)
            throw new InvalidOperationException("لا يمكن حذف فاتورة المخالصة لحماية السجلات المحاسبية");

        await using var transaction = await BeginInvoiceTransactionAsync(invoice.SemesterId);

        if (invoice.Type != ClearanceType)
        {
            await EnsureNoClearanceAsync(invoice.LibraryId, invoice.SemesterId);
        }

        if (invoice.Type == OrderType)
        {
            var refundableQuantities = await GetRefundableQuantitiesAsync(invoice.LibraryId, invoice.SemesterId);
            foreach (var item in invoice.Items)
            {
                refundableQuantities.TryGetValue(item.BookId, out var available);
                if (available < item.Quantity)
                    throw new InvalidOperationException($"لا يمكن حذف الفاتورة لوجود مرتجعات مسجلة. يرجى حذف المرتجعات المتعلقة أولاً.");
            }
        }

        if (invoice.Type == RefundType)
        {
            // Check stock before deleting a refund (it would reduce stock)
            foreach (var item in invoice.Items)
            {
                var book = await _db.Books.FindAsync(item.BookId);
                if (book != null && book.StockQuantity < item.Quantity)
                    throw new InvalidOperationException($"لا يمكن حذف المرتجع: المخزون الحالي للكتاب «{book.Name}» ({book.StockQuantity}) أقل من كمية المرتجع ({item.Quantity})");
            }
        }

        foreach (var item in invoice.Items)
        {
            if (invoice.Type == OrderType)
            {
                await _db.Books
                    .Where(b => b.Id == item.BookId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(b => b.StockQuantity, b => b.StockQuantity + item.Quantity));
            }
            else if (invoice.Type == RefundType)
            {
                await _db.Books
                    .Where(b => b.Id == item.BookId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(b => b.StockQuantity, b => b.StockQuantity - item.Quantity));
            }
        }

        _db.Invoices.Remove(invoice);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private async Task<IDbContextTransaction> BeginInvoiceTransactionAsync(int semesterId)
    {
        var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await AcquireTransactionLockAsync($"invoice-semester:{semesterId}", transaction);
        return transaction;
    }

    private async Task AcquireTransactionLockAsync(string resource, IDbContextTransaction transaction)
    {
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = @timeout;
            SELECT @result;
            """;

        var resourceParameter = command.CreateParameter();
        resourceParameter.ParameterName = "@resource";
        resourceParameter.Value = resource;
        command.Parameters.Add(resourceParameter);

        var timeoutParameter = command.CreateParameter();
        timeoutParameter.ParameterName = "@timeout";
        timeoutParameter.Value = 10000;
        command.Parameters.Add(timeoutParameter);

        var result = Convert.ToInt32(await command.ExecuteScalarAsync());
        if (result < 0)
            throw new InvalidOperationException("تعذر قفل عملية الفاتورة. الرجاء المحاولة مرة أخرى");
    }

    private async Task<Semester> GetSemesterAsync(int semesterId)
    {
        return await _db.Semesters.FindAsync(semesterId)
            ?? throw new InvalidOperationException("الفصل الدراسي غير موجود");
    }

    private async Task<Library> GetActiveLibraryAsync(int libraryId)
    {
        var library = await _db.Libraries.FindAsync(libraryId)
            ?? throw new InvalidOperationException("المكتبة غير موجودة");

        if (!library.IsActive)
            throw new InvalidOperationException("لا يمكن إنشاء فاتورة لمكتبة غير نشطة");

        return library;
    }

    private async Task<Library> GetLibraryForClearanceAsync(int libraryId)
    {
        return await _db.Libraries.FindAsync(libraryId)
            ?? throw new InvalidOperationException("المكتبة غير موجودة");
    }

    private async Task EnsureNoClearanceAsync(int libraryId, int semesterId)
    {
        var hasClearance = await _db.Invoices.AnyAsync(i =>
            i.LibraryId == libraryId &&
            i.SemesterId == semesterId &&
            i.Type == ClearanceType);

        if (hasClearance)
            throw new InvalidOperationException("لا يمكن إنشاء طلب أو مرتجع بعد إصدار المخالصة النهائية");
    }

    private async Task<Dictionary<int, Book>> LoadBooksAsync(IEnumerable<int> bookIds, int semesterId)
    {
        var ids = bookIds.ToList();
        var books = await _db.Books
            .Where(b => ids.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id);

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

    private async Task<Dictionary<int, int>> GetRefundableQuantitiesAsync(int libraryId, int semesterId)
    {
        var invoices = await _db.Invoices
            .Include(i => i.Items)
            .Where(i => i.LibraryId == libraryId && i.SemesterId == semesterId)
            .ToListAsync();

        return invoices
            .Where(i => i.Type is OrderType or RefundType)
            .SelectMany(i => i.Items.Select(item => new { i.Type, item.BookId, item.Quantity }))
            .GroupBy(x => x.BookId)
            .ToDictionary(
                g => g.Key,
                g => g.Where(x => x.Type == OrderType).Sum(x => x.Quantity)
                   - g.Where(x => x.Type == RefundType).Sum(x => x.Quantity));
    }

    /// <summary>
    /// Gets original sale prices using FIFO from order invoices.
    /// Returns the first (earliest) sale price for each book.
    /// </summary>
    private async Task<Dictionary<int, decimal>> GetOriginalSalePricesAsync(int libraryId, int semesterId)
    {
        var orderItems = await _db.Invoices
            .Where(i => i.LibraryId == libraryId && i.SemesterId == semesterId && i.Type == OrderType)
            .OrderBy(i => i.Date)
            .SelectMany(i => i.Items)
            .ToListAsync();

        var prices = new Dictionary<int, decimal>();
        foreach (var item in orderItems)
        {
            // First sale price wins (FIFO)
            if (!prices.ContainsKey(item.BookId))
                prices[item.BookId] = item.UnitPrice;
        }
        return prices;
    }

    private async Task<Invoice> CreateInvoiceAsync(
        Semester semester,
        Library library,
        string type,
        decimal totalAmount,
        List<InvoiceItem> invoiceItems)
    {
        var invoiceYear = GetCurrentInvoiceYear();
        
        // Determine the term code for the invoice number:
        // - Orders and refunds use the semester code (A/B)
        // - Clearance uses "P"
        var termCode = type == ClearanceType ? "P" : semester.Code;
        
        var nextNumber = await GetNextInvoiceNumberAsync(library.Id, invoiceYear, termCode);
        var invoice = new Invoice
        {
            InvoiceNumber = nextNumber,
            InvoiceYear = invoiceYear,
            TermCode = termCode,
            Type = type,
            LibraryId = library.Id,
            SemesterId = semester.Id,
            Date = DateTime.UtcNow,
            TotalAmount = totalAmount,
            PrintStatus = PendingPrintStatus,
            ResponsibleName = library.ResponsibleName,
            ResponsiblePhone = library.ResponsiblePhone,
            Items = invoiceItems
        };

        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();
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

    private static List<InvoiceItem> BuildClearanceItems(List<Invoice> libraryInvoices, out decimal totalAmount)
    {
        var summaries = new Dictionary<(int BookId, decimal Price), ClearanceSummary>();

        foreach (var invoice in libraryInvoices)
        {
            foreach (var item in invoice.Items)
            {
                var key = (item.BookId, item.UnitPrice);
                if (!summaries.TryGetValue(key, out var summary))
                {
                    summary = new ClearanceSummary(item.BookId, item.BookName, item.BookGrade, item.UnitPrice);
                    summaries[key] = summary;
                }

                if (invoice.Type == OrderType)
                    summary.OrderedQty += item.Quantity;
                else if (invoice.Type == RefundType)
                    summary.RefundedQty += item.Quantity;
            }
        }

        totalAmount = 0;
        var items = new List<InvoiceItem>();

        foreach (var summary in summaries.Values)
        {
            var netQty = summary.OrderedQty - summary.RefundedQty;
            if (netQty <= 0)
                continue;

            var lineTotal = netQty * summary.Price;
            totalAmount += lineTotal;

            items.Add(new InvoiceItem
            {
                BookId = summary.BookId,
                BookName = summary.Name,
                BookGrade = summary.Grade,
                Quantity = netQty,
                UnitPrice = summary.Price,
                Total = lineTotal
            });
        }

        return items;
    }

    private sealed class ClearanceSummary
    {
        public ClearanceSummary(int bookId, string name, string grade, decimal price)
        {
            BookId = bookId;
            Name = name;
            Grade = grade;
            Price = price;
        }

        public int BookId { get; }
        public string Name { get; }
        public string Grade { get; }
        public decimal Price { get; }
        public int OrderedQty { get; set; }
        public int RefundedQty { get; set; }
    }
}
