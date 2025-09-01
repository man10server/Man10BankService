using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ServerLoanController(ServerLoanService service) : ControllerBase
{
    public sealed class PaymentAmountRequest
    {
        public decimal PaymentAmount { get; set; }
    }

    [HttpGet("{uuid}")]
    public async Task<IActionResult> GetByUuid([FromRoute] string uuid)
    {
        var res = await service.GetByUuidAsync(uuid);
        return StatusCode(res.StatusCode, res);
    }

public sealed class BorrowRequest
{
    public string Player { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

[HttpPost("{uuid}/borrow")]
public async Task<IActionResult> Borrow([FromRoute] string uuid, [FromBody] BorrowRequest request)
{
    if (!ModelState.IsValid) return ValidationProblem(ModelState);
    var res = await service.BorrowAsync(new ServerLoanBorrowRequest
    {
        Uuid = uuid,
        Player = request.Player,
        Amount = request.Amount
    });
    return StatusCode(res.StatusCode, res);
}

    [HttpPost("{uuid}/repay")]
    public async Task<IActionResult> Repay([FromRoute] string uuid, [FromQuery] decimal? amount)
    {
        var res = await service.RepayAsync(uuid, amount);
        return StatusCode(res.StatusCode, res);
    }

    [HttpPost("{uuid}/payment-amount")]
    public async Task<IActionResult> SetPaymentAmount([FromRoute] string uuid, [FromBody] PaymentAmountRequest request)
    {
        var res = await service.SetPaymentAmountAsync(uuid, request.PaymentAmount);
        return StatusCode(res.StatusCode, res);
    }

    [HttpGet("{uuid}/borrow-limit")]
    public async Task<IActionResult> GetBorrowLimit([FromRoute] string uuid, [FromQuery] int? window)
    {
        var res = await service.CalculateBorrowLimitAsync(uuid, window);
        return StatusCode(res.StatusCode, res);
    }
}
