using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BookDistributionAPI.Common;
using BookDistributionAPI.Data;

namespace BookDistributionAPI.Features.Invoices;

[ApiController]
[Route("api/invoices")]
public class InvoicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly InvoiceBusinessService _invoiceService;

    public InvoicesController(AppDbContext db, InvoiceBusinessService invoiceService)
    {
        _db = db;
        _invoiceService = invoiceService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? type,
        [FromQuery] int? semesterId,
        [FromQuery] int? libraryId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate)
    {
        var query = _db.Invoices
            .Include(i => i.Items)
            .Include(i => i.Library).ThenInclude(l => l.Governorate)
            .Include(i => i.Library).ThenInclude(l => l.City)
            .Include(i => i.Semester)
            .AsQueryable();

        if (!string.IsNullOrEmpty(type))
            query = query.Where(i => i.Type == type);
        if (semesterId.HasValue)
            query = query.Where(i => i.SemesterId == semesterId.Value);
        if (libraryId.HasValue)
            query = query.Where(i => i.LibraryId == libraryId.Value);
        if (fromDate.HasValue)
            query = query.Where(i => i.Date >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(i => i.Date <= toDate.Value);

        var invoices = await query
            .OrderByDescending(i => i.Date)
            .ToListAsync();

        var result = invoices.Select(InvoiceBusinessService.ToDto).ToList();
        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Items)
            .Include(i => i.Library).ThenInclude(l => l.Governorate)
            .Include(i => i.Library).ThenInclude(l => l.City)
            .Include(i => i.Semester)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (invoice == null) return NotFound(ApiResponse<object>.Fail("الفاتورة غير موجودة"));
        return Ok(ApiResponse<object>.Ok(InvoiceBusinessService.ToDto(invoice)));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await _invoiceService.DeleteInvoiceAsync(id);
            return Ok(ApiResponse<bool>.Ok(true, "تم حذف الفاتورة بنجاح"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }

    [HttpPost("order")]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderDto dto)
    {
        try
        {
            var invoice = await _invoiceService.CreateOrderAsync(dto);

            var loaded = await _db.Invoices
                .Include(i => i.Items)
                .Include(i => i.Library).ThenInclude(l => l.Governorate)
                .Include(i => i.Library).ThenInclude(l => l.City)
                .Include(i => i.Semester)
                .FirstOrDefaultAsync(i => i.Id == invoice.Id);

            if (loaded == null)
                return NotFound(ApiResponse<object>.Fail("الفاتورة غير موجودة"));

            return Ok(ApiResponse<object>.Ok(InvoiceBusinessService.ToDto(loaded), "تم إنشاء فاتورة الطلب بنجاح"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }

    [HttpPost("refund")]
    public async Task<IActionResult> CreateRefund([FromBody] CreateRefundDto dto)
    {
        try
        {
            var invoice = await _invoiceService.CreateRefundAsync(dto);

            var loaded = await _db.Invoices
                .Include(i => i.Items)
                .Include(i => i.Library).ThenInclude(l => l.Governorate)
                .Include(i => i.Library).ThenInclude(l => l.City)
                .Include(i => i.Semester)
                .FirstOrDefaultAsync(i => i.Id == invoice.Id);

            if (loaded == null)
                return NotFound(ApiResponse<object>.Fail("الفاتورة غير موجودة"));

            return Ok(ApiResponse<object>.Ok(InvoiceBusinessService.ToDto(loaded), "تم إنشاء فاتورة المرتجع بنجاح"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }

    [HttpPost("clearance")]
    public async Task<IActionResult> CreateClearance([FromBody] CreateClearanceDto dto)
    {
        try
        {
            var invoice = await _invoiceService.CreateClearanceAsync(dto);

            var loaded = await _db.Invoices
                .Include(i => i.Items)
                .Include(i => i.Library).ThenInclude(l => l.Governorate)
                .Include(i => i.Library).ThenInclude(l => l.City)
                .Include(i => i.Semester)
                .FirstOrDefaultAsync(i => i.Id == invoice.Id);

            if (loaded == null)
                return NotFound(ApiResponse<object>.Fail("الفاتورة غير موجودة"));

            return Ok(ApiResponse<object>.Ok(InvoiceBusinessService.ToDto(loaded), "تم إنشاء فاتورة المخالصة بنجاح"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }

    [HttpPost("clearance/batch")]
    public async Task<IActionResult> CreateBatchClearances([FromBody] CreateBatchClearanceDto dto)
    {
        try
        {
            var result = await _invoiceService.CreateBatchClearancesAsync(dto.SemesterId);
            return Ok(ApiResponse<object>.Ok(result, $"تم إنشاء {result.Count} مخالصة بنجاح"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }

    [HttpGet("clearance/preview")]
    public async Task<IActionResult> GetClearancePreview(
        [FromQuery] int? libraryId, 
        [FromQuery] int semesterId)
    {
        try
        {
            var preview = await _invoiceService.GetClearancePreviewAsync(libraryId, semesterId);
            return Ok(ApiResponse<object>.Ok(preview));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }

    [HttpGet("next-number")]
    public async Task<IActionResult> GetNextNumber([FromQuery] int libraryId, [FromQuery] int semesterId, [FromQuery] string type = "order")
    {
        var semester = await _db.Semesters.FindAsync(semesterId);
        if (semester == null) return NotFound(ApiResponse<object>.Fail("الفصل الدراسي غير موجود"));

        var invoiceYear = DateTime.UtcNow.Year;
        var termCode = type == "clearance" ? "P" : semester.Code;
        var nextNumber = await _invoiceService.GetNextInvoiceNumberAsync(libraryId, invoiceYear, termCode);

        return Ok(ApiResponse<object>.Ok(new
        {
            NextNumber = nextNumber,
            TermCode = termCode,
            DisplayNumber = InvoiceBusinessService.FormatDisplayNumber(invoiceYear, termCode, nextNumber)
        }));
    }

    [HttpPut("{id}/print-status")]
    public async Task<IActionResult> UpdatePrintStatus(int id, [FromBody] UpdatePrintStatusDto dto)
    {
        var invoice = await _db.Invoices.FindAsync(id);
        if (invoice == null) return NotFound(ApiResponse<object>.Fail("الفاتورة غير موجودة"));

        invoice.PrintStatus = dto.PrintStatus;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(true, "تم تحديث حالة الطباعة"));
    }
}
