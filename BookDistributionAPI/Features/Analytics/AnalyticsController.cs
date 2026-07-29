using BookDistributionAPI.Common;
using BookDistributionAPI.Data;
using BookDistributionAPI.Features.Books;
using BookDistributionAPI.Features.Invoices;
using BookDistributionAPI.Features.ReceiptVouchers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookDistributionAPI.Features.Analytics;

[ApiController]
[Authorize]
[Route("api/analytics")]
public class AnalyticsController : ControllerBase
{
    private const string OrderType = "order";
    private const string RefundType = "refund";
    private const int LowStockThreshold = 200;
    private const int CriticalStockThreshold = 150;

    private readonly AppDbContext _db;

    public AnalyticsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] int? semesterId,
        [FromQuery] int? academicYearId,
        CancellationToken cancellationToken)
    {
        var invoices = FilterInvoices(_db.Invoices.AsNoTracking(), semesterId, academicYearId)
            .Where(invoice => invoice.Type == OrderType || invoice.Type == RefundType);

        var invoiceItems = FilterInvoiceItems(_db.InvoiceItems.AsNoTracking(), semesterId, academicYearId)
            .Where(item => item.Invoice.Type == OrderType || item.Invoice.Type == RefundType);

        var totalsByType = await invoices
            .GroupBy(invoice => invoice.Type)
            .Select(group => new
            {
                Type = group.Key,
                Amount = group.Sum(invoice => invoice.TotalAmount),
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        var quantitiesByType = await invoiceItems
            .GroupBy(item => item.Invoice.Type)
            .Select(group => new
            {
                Type = group.Key,
                Quantity = group.Sum(item => item.Quantity)
            })
            .ToListAsync(cancellationToken);

        var orderTotal = totalsByType.FirstOrDefault(item => item.Type == OrderType)?.Amount ?? 0;
        var refundTotal = totalsByType.FirstOrDefault(item => item.Type == RefundType)?.Amount ?? 0;
        var orderCount = totalsByType.FirstOrDefault(item => item.Type == OrderType)?.Count ?? 0;
        var refundCount = totalsByType.FirstOrDefault(item => item.Type == RefundType)?.Count ?? 0;
        var orderQuantity = quantitiesByType.FirstOrDefault(item => item.Type == OrderType)?.Quantity ?? 0;
        var refundQuantity = quantitiesByType.FirstOrDefault(item => item.Type == RefundType)?.Quantity ?? 0;

        var vouchers = FilterVouchers(_db.ReceiptVouchers.AsNoTracking(), semesterId, academicYearId);
        var totalCollected = await vouchers.SumAsync(voucher => (decimal?)voucher.Amount, cancellationToken) ?? 0;

        var stockQuery = FilterBooks(_db.Books.AsNoTracking(), semesterId, academicYearId);
        var totalItems = await stockQuery.SumAsync(book => (int?)book.StockQuantity, cancellationToken) ?? 0;
        var lowStockCount = await stockQuery.CountAsync(book => book.StockQuantity < LowStockThreshold, cancellationToken);
        var totalLibraries = await _db.Libraries.AsNoTracking().CountAsync(cancellationToken);

        var criticalStock = await stockQuery
            .Where(book => book.StockQuantity < CriticalStockThreshold)
            .OrderBy(book => book.StockQuantity)
            .ThenBy(book => book.Name)
            .Take(20)
            .Select(book => new DashboardCriticalStockDto
            {
                Id = book.Id,
                Name = book.Name,
                Grade = book.Grade,
                StockQuantity = book.StockQuantity
            })
            .ToListAsync(cancellationToken);

        var demandByBook = await invoiceItems
            .Where(item => item.Invoice.Type == OrderType)
            .GroupBy(item => item.BookId)
            .Select(group => new { BookId = group.Key, Demand = group.Sum(item => item.Quantity) })
            .ToDictionaryAsync(item => item.BookId, item => item.Demand, cancellationToken);

        foreach (var book in criticalStock)
            book.Demand = demandByBook.GetValueOrDefault(book.Id, 0);

        var mostRefunded = await invoiceItems
            .Where(item => item.Invoice.Type == RefundType)
            .GroupBy(item => item.BookName)
            .Select(group => new DashboardBookCountDto
            {
                Name = group.Key,
                Count = group.Sum(item => item.Quantity)
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Name)
            .Take(5)
            .ToListAsync(cancellationToken);

        var salesByTerm = await invoices
            .GroupBy(invoice => new { invoice.TermCode, invoice.Semester.Name })
            .Select(group => new DashboardTermSalesDto
            {
                TermCode = group.Key.TermCode,
                TermName = group.Key.Name,
                Revenue = group.Sum(invoice => invoice.Type == OrderType ? invoice.TotalAmount : -invoice.TotalAmount)
            })
            .OrderByDescending(item => item.Revenue)
            .ToListAsync(cancellationToken);

        var invoiceTotalsByLibrary = await invoices
            .GroupBy(invoice => new { invoice.LibraryId, invoice.LibraryName })
            .Select(group => new
            {
                group.Key.LibraryId,
                group.Key.LibraryName,
                OrderTotal = group.Where(invoice => invoice.Type == OrderType).Sum(invoice => (decimal?)invoice.TotalAmount) ?? 0,
                RefundTotal = group.Where(invoice => invoice.Type == RefundType).Sum(invoice => (decimal?)invoice.TotalAmount) ?? 0
            })
            .ToListAsync(cancellationToken);

        var libraryIds = invoiceTotalsByLibrary.Select(item => item.LibraryId).ToList();
        var paidByLibrary = await vouchers
            .Where(voucher => libraryIds.Contains(voucher.LibraryId))
            .GroupBy(voucher => voucher.LibraryId)
            .Select(group => new { LibraryId = group.Key, PaidAmount = group.Sum(voucher => voucher.Amount) })
            .ToDictionaryAsync(item => item.LibraryId, item => item.PaidAmount, cancellationToken);

        var libraryBalances = invoiceTotalsByLibrary
            .Select(item =>
            {
                paidByLibrary.TryGetValue(item.LibraryId, out var paidAmount);
                var totalAmount = item.OrderTotal - item.RefundTotal;
                return new DashboardLibraryBalanceDto
                {
                    LibraryId = item.LibraryId,
                    LibraryName = item.LibraryName,
                    TotalAmount = totalAmount,
                    PaidAmount = paidAmount,
                    Balance = totalAmount - paidAmount
                };
            })
            .Where(item => item.Balance != 0)
            .OrderByDescending(item => item.Balance)
            .ToList();

        var allInvoiceItems = _db.InvoiceItems
            .AsNoTracking()
            .Where(item => item.Invoice.Type == OrderType || item.Invoice.Type == RefundType);

        var classicSalesByYear = await allInvoiceItems
            .GroupBy(item => item.Invoice.Semester.AcademicYear.Name)
            .Select(group => new DashboardYearSalesDto
            {
                AcademicYear = group.Key,
                Revenue = group.Sum(item => item.Invoice.Type == OrderType ? item.Total : -item.Total),
                Quantity = group.Sum(item => item.Invoice.Type == OrderType ? item.Quantity : -item.Quantity)
            })
            .OrderBy(item => item.AcademicYear)
            .ToListAsync(cancellationToken);

        var classicLibraryMetrics = await allInvoiceItems
            .GroupBy(item => new
            {
                AcademicYear = item.Invoice.Semester.AcademicYear.Name,
                item.Invoice.TermCode,
                SemesterName = item.Invoice.Semester.Name,
                item.Invoice.LibraryId,
                item.Invoice.LibraryName
            })
            .Select(group => new ClassicLibraryMetric
            {
                AcademicYear = group.Key.AcademicYear,
                TermCode = group.Key.TermCode,
                TermName = group.Key.SemesterName,
                LibraryName = group.Key.LibraryName,
                Ordered = group.Sum(item => item.Invoice.Type == OrderType ? item.Quantity : 0),
                Refunded = group.Sum(item => item.Invoice.Type == RefundType ? item.Quantity : 0),
                NetRevenue = group.Sum(item => item.Invoice.Type == OrderType ? item.Total : -item.Total)
            })
            .ToListAsync(cancellationToken);

        var classicRows = classicLibraryMetrics
            .GroupBy(item => new { item.AcademicYear, item.TermCode, item.TermName })
            .Select(group => new DashboardClassicRowDto
            {
                AcademicYear = group.Key.AcademicYear,
                TermCode = group.Key.TermCode,
                TermName = group.Key.TermName,
                BestLibraryByRevenue = group
                    .OrderByDescending(item => item.NetRevenue)
                    .ThenBy(item => item.LibraryName)
                    .First().LibraryName,
                BestLibraryByQuantity = group
                    .OrderByDescending(item => item.Ordered - item.Refunded)
                    .ThenBy(item => item.LibraryName)
                    .First().LibraryName,
                LibraryCount = group.Count(),
                Ordered = group.Sum(item => item.Ordered),
                Refunded = group.Sum(item => item.Refunded),
                NetRevenue = group.Sum(item => item.NetRevenue),
                NetQuantity = group.Sum(item => item.Ordered - item.Refunded)
            })
            .OrderByDescending(item => item.AcademicYear)
            .ThenByDescending(item => item.TermCode)
            .ToList();

        return Ok(ApiResponse<DashboardAnalyticsDto>.Ok(new DashboardAnalyticsDto
        {
            TotalLibraries = totalLibraries,
            TotalItems = totalItems,
            LowStockCount = lowStockCount,
            TotalRevenue = orderTotal - refundTotal,
            TotalCollected = totalCollected,
            TotalOutstanding = orderTotal - refundTotal - totalCollected,
            TotalItemsSold = orderQuantity - refundQuantity,
            OrderCount = orderCount,
            RefundCount = refundCount,
            CriticalStock = criticalStock,
            LibraryBalances = libraryBalances,
            MostRefunded = mostRefunded,
            SalesByTerm = salesByTerm,
            ClassicSalesByYear = classicSalesByYear,
            ClassicRows = classicRows
        }));
    }

    private static IQueryable<Invoice> FilterInvoices(IQueryable<Invoice> query, int? semesterId, int? academicYearId)
    {
        if (semesterId.HasValue)
            return query.Where(invoice => invoice.SemesterId == semesterId.Value);

        if (academicYearId.HasValue)
            return query.Where(invoice => invoice.Semester.AcademicYearId == academicYearId.Value);

        return query;
    }

    private static IQueryable<InvoiceItem> FilterInvoiceItems(IQueryable<InvoiceItem> query, int? semesterId, int? academicYearId)
    {
        if (semesterId.HasValue)
            return query.Where(item => item.Invoice.SemesterId == semesterId.Value);

        if (academicYearId.HasValue)
            return query.Where(item => item.Invoice.Semester.AcademicYearId == academicYearId.Value);

        return query;
    }

    private static IQueryable<ReceiptVoucher> FilterVouchers(
        IQueryable<ReceiptVoucher> query,
        int? semesterId,
        int? academicYearId)
    {
        if (semesterId.HasValue)
            return query.Where(voucher => voucher.SemesterId == semesterId.Value);

        if (academicYearId.HasValue)
            return query.Where(voucher => voucher.Semester != null && voucher.Semester.AcademicYearId == academicYearId.Value);

        return query;
    }

    private static IQueryable<Book> FilterBooks(IQueryable<Book> query, int? semesterId, int? academicYearId)
    {
        if (semesterId.HasValue)
            return query.Where(book => book.SemesterId == semesterId.Value);

        if (academicYearId.HasValue)
            return query.Where(book => book.Semester.AcademicYearId == academicYearId.Value);

        return query;
    }

    private sealed class ClassicLibraryMetric
    {
        public string AcademicYear { get; init; } = string.Empty;
        public string TermCode { get; init; } = string.Empty;
        public string TermName { get; init; } = string.Empty;
        public string LibraryName { get; init; } = string.Empty;
        public int Ordered { get; init; }
        public int Refunded { get; init; }
        public decimal NetRevenue { get; init; }
    }
}
