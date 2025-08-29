using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BankController(BankService service) : ControllerBase
{
    [HttpGet("{uuid}/balance")]
    public async Task<IActionResult> GetBalance([FromRoute] string uuid)
    {
        var res = await service.GetBalanceAsync(uuid);
        return StatusCode(res.StatusCode, res);
    }

    [HttpGet("{uuid}/logs")]
    public async Task<IActionResult> GetLogs([FromRoute] string uuid, [FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var res = await service.GetLogsAsync(uuid, limit, offset);
        return StatusCode(res.StatusCode, res);
    }

    [HttpPost("deposit")]
    public async Task<IActionResult> Deposit([FromBody] DepositRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var res = await service.DepositAsync(request);
        return StatusCode(res.StatusCode, res);
    }

    [HttpPost("withdraw")]
    public async Task<IActionResult> Withdraw([FromBody] WithdrawRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var res = await service.WithdrawAsync(request);
        return StatusCode(res.StatusCode, res);
    }
}

