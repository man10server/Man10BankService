using Man10BankService.Services;
using Microsoft.AspNetCore.Mvc;
using Man10BankService.Models.Requests;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]/{uuid}")]
public class ServerLoanController(ServerLoanService service) : ControllerBase
{
    public sealed class PaymentAmountRequest
    {
        public decimal PaymentAmount { get; set; }
    }

    [HttpGet("")]
    public async Task<IActionResult> GetByUuid([FromRoute] string uuid)
    {
        var res = await service.GetByUuidAsync(uuid);
        return StatusCode(res.StatusCode, res);
    }

[HttpPost("borrow")]
public async Task<IActionResult> Borrow([FromRoute] string uuid, [FromBody] ServerLoanBorrowBodyRequest request)
{
    if (!ModelState.IsValid) return ValidationProblem(ModelState);
    var res = await service.BorrowAsync(uuid, request.Player, request.Amount);
    return StatusCode(res.StatusCode, res);
}

    [HttpPost("repay")]
    public async Task<IActionResult> Repay([FromRoute] string uuid, [FromQuery] decimal? amount)
    {
        var res = await service.RepayAsync(uuid, amount);
        return StatusCode(res.StatusCode, res);
    }

    [HttpPost("payment-amount")]
    public async Task<IActionResult> SetPaymentAmount([FromRoute] string uuid, [FromBody] PaymentAmountRequest request)
    {
        var res = await service.SetPaymentAmountAsync(uuid, request.PaymentAmount);
        return StatusCode(res.StatusCode, res);
    }

    [HttpGet("borrow-limit")]
    public async Task<IActionResult> GetBorrowLimit([FromRoute] string uuid, [FromQuery] int? window)
    {
        var res = await service.CalculateBorrowLimitAsync(uuid, window);
        return StatusCode(res.StatusCode, res);
    }
}
