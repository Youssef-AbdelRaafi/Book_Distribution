using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BookDistributionAPI.Common;
using System.Threading;

namespace BookDistributionAPI.Features.ReceiptVouchers;

[ApiController]
[Route("api/receipt-vouchers")]
[Authorize]
public class ReceiptVouchersController : ControllerBase
{
    private readonly ReceiptVoucherBusinessService _service;

    public ReceiptVouchersController(ReceiptVoucherBusinessService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? libraryId,
        [FromQuery] int? semesterId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken cancellationToken)
    {
        var vouchers = await _service.GetAllAsync(libraryId, semesterId, fromDate, toDate, cancellationToken);
        var dtos = vouchers.Select(ReceiptVoucherBusinessService.ToDto).ToList();
        return Ok(ApiResponse<List<ReceiptVoucherDto>>.Ok(dtos));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var voucher = await _service.GetByIdAsync(id, cancellationToken);
        if (voucher == null)
            return NotFound(ApiResponse<object>.Fail("سند القبض غير موجود"));

        return Ok(ApiResponse<ReceiptVoucherDto>.Ok(ReceiptVoucherBusinessService.ToDto(voucher)));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateReceiptVoucherDto dto, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<object>.Fail("بيانات غير صالحة"));

        try
        {
            var voucher = await _service.CreateAsync(dto, cancellationToken);
            return Ok(ApiResponse<ReceiptVoucherDto>.Ok(ReceiptVoucherBusinessService.ToDto(voucher)));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            await _service.DeleteAsync(id, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { }, "تم حذف سند القبض بنجاح"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }
}
