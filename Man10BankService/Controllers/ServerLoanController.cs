using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]/{uuid}")]
public class ServerLoanController(ServerLoanService service) : ControllerBase
{
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
    public async Task<IActionResult> SetPaymentAmount([FromRoute] string uuid, [FromQuery] decimal paymentAmount)
    {
        var res = await service.SetPaymentAmountAsync(uuid, paymentAmount);
        return StatusCode(res.StatusCode, res);
    }

    [HttpGet("borrow-limit")]
    public async Task<IActionResult> GetBorrowLimit([FromRoute] string uuid)
    {
        var res = await service.CalculateBorrowLimitAsync(uuid);
        return StatusCode(res.StatusCode, res);
    }

    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs([FromRoute] string uuid, [FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var res = await service.GetLogsAsync(uuid, limit, offset);
        return StatusCode(res.StatusCode, res);
    }
}
