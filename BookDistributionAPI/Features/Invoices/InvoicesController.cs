using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BookDistributionAPI.Common;
using BookDistributionAPI.Data;
using System.Threading;

namespace BookDistributionAPI.Features.Invoices;

[ApiController]
[Route("api/invoices")]
public class InvoicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly InvoiceBusinessService _invoiceService;
    private readonly IAcademicYearHelper _academicYearHelper;

    public InvoicesController(AppDbContext db, InvoiceBusinessService invoiceService, IAcademicYearHelper academicYearHelper)
    {
        _db = db;
        _invoiceService = invoiceService;
        _academicYearHelper = academicYearHelper;
    }

    private IQueryable<Invoice> InvoiceQuery()
    {
        return _db.Invoices
            .Include(i => i.Items)
            .Include(i => i.Library).ThenInclude(l => l.Governorate)
            .Include(i => i.Library).ThenInclude(l => l.City)
            .Include(i => i.Semester);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? type,
        [FromQuery] int? semesterId,
        [FromQuery] int? libraryId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken cancellationToken)
    {
        var activeSemesterIds = await _academicYearHelper.GetActiveSemesterIdsAsync(cancellationToken);
        var query = InvoiceQuery().AsQueryable();

        if (!string.IsNullOrEmpty(type))
            query = query.Where(i => i.Type == type);
        if (semesterId.HasValue)
            query = query.Where(i => i.SemesterId == semesterId.Value);
        else if (activeSemesterIds.Count > 0)
            query = query.Where(i => activeSemesterIds.Contains(i.SemesterId));
        if (libraryId.HasValue)
            query = query.Where(i => i.LibraryId == libraryId.Value);
        if (fromDate.HasValue)
            query = query.Where(i => i.Date >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(i => i.Date <= toDate.Value);

        var invoices = await query
            .OrderByDescending(i => i.Date)
            .ToListAsync(cancellationToken);

        var result = invoices.Select(InvoiceBusinessService.ToDto).ToList();
        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var invoice = await InvoiceQuery()
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (invoice == null) return NotFound(ApiResponse<object>.Fail("الفاتورة غير موجودة"));
        return Ok(ApiResponse<object>.Ok(InvoiceBusinessService.ToDto(invoice)));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            await _invoiceService.DeleteInvoiceAsync(id, cancellationToken);
            return Ok(ApiResponse<bool>.Ok(true, "تم حذف الفاتورة بنجاح"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }

    [HttpPost("order")]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var invoice = await _invoiceService.CreateOrderAsync(dto, cancellationToken);

            var loaded = await InvoiceQuery()
                .FirstOrDefaultAsync(i => i.Id == invoice.Id, cancellationToken);

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
    public async Task<IActionResult> CreateRefund([FromBody] CreateRefundDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var invoice = await _invoiceService.CreateRefundAsync(dto, cancellationToken);

            var loaded = await InvoiceQuery()
                .FirstOrDefaultAsync(i => i.Id == invoice.Id, cancellationToken);

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
    public async Task<IActionResult> CreateClearance([FromBody] CreateClearanceDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var invoice = await _invoiceService.CreateClearanceAsync(dto, cancellationToken);

            var loaded = await InvoiceQuery()
                .FirstOrDefaultAsync(i => i.Id == invoice.Id, cancellationToken);

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
    public async Task<IActionResult> CreateBatchClearances([FromBody] CreateBatchClearanceDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _invoiceService.CreateBatchClearancesAsync(dto.SemesterId, cancellationToken);
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
        [FromQuery] int semesterId,
        CancellationToken cancellationToken)
    {
        try
        {
            var preview = await _invoiceService.GetClearancePreviewAsync(libraryId, semesterId, cancellationToken);
            return Ok(ApiResponse<object>.Ok(preview));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }

    [HttpGet("next-number")]
    public async Task<IActionResult> GetNextNumber([FromQuery] int libraryId, [FromQuery] int semesterId, CancellationToken cancellationToken, [FromQuery] string type = "order")
    {
        var semester = await _db.Semesters
            .Include(s => s.AcademicYear)
            .FirstOrDefaultAsync(s => s.Id == semesterId, cancellationToken);
        if (semester == null) return NotFound(ApiResponse<object>.Fail("الفصل الدراسي غير موجود"));
        if (semester.AcademicYear == null) return NotFound(ApiResponse<object>.Fail("البيانات الأكاديمية غير متوفرة"));

        if (!int.TryParse(semester.AcademicYear.Name.Split('-')[0], out var invoiceYear))
            return BadRequest(ApiResponse<object>.Fail("صيغة السنة الدراسية غير صالحة"));
        var termCode = type == "clearance" ? InvoiceBusinessService.ClearanceTermCode : semester.Code;
        var nextNumber = await _invoiceService.GetNextInvoiceNumberAsync(libraryId, invoiceYear, cancellationToken);

        return Ok(ApiResponse<object>.Ok(new
        {
            NextNumber = nextNumber,
            TermCode = termCode,
            DisplayNumber = InvoiceBusinessService.FormatDisplayNumber(invoiceYear, termCode, nextNumber)
        }));
    }

    [HttpPut("{id}/print-status")]
    public async Task<IActionResult> UpdatePrintStatus(int id, [FromBody] UpdatePrintStatusDto dto, CancellationToken cancellationToken)
    {
        var invoice = await _db.Invoices.FindAsync([id], cancellationToken);
        if (invoice == null) return NotFound(ApiResponse<object>.Fail("الفاتورة غير موجودة"));

        invoice.PrintStatus = dto.PrintStatus;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponse<object>.Ok(true, "تم تحديث حالة الطباعة"));
    }
}
