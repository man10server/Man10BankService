using Man10BankService.Models.Requests;
using Man10BankService.Models.Responses;
using Man10BankService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BankController(BankService service) : ControllerBase
{
    [HttpGet("{uuid:uuid}/balance")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(BalanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<BalanceResponse>> GetBalance([FromRoute] string uuid)
    {
        var res = await service.GetBalanceAsync(uuid);
        return this.ToActionResult(res, balance => new BalanceResponse(balance));
    }

    [HttpGet("{uuid:uuid}/logs")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(List<MoneyLogResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<MoneyLogResponse>>> GetLogs([FromRoute] string uuid, [FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var res = await service.GetLogsAsync(uuid, limit, offset);
        return this.ToActionResult(res, logs => logs.Select(MoneyLogResponse.From).ToList());
    }

    [HttpPost("deposit")]
    [Authorize(Policy = "RequireWriteScope")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(BalanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<BalanceResponse>> Deposit([FromBody] DepositRequest request)
    {
        var res = await service.DepositAsync(request);
        return this.ToActionResult(res, balance => new BalanceResponse(balance));
    }

    [HttpPost("withdraw")]
    [Authorize(Policy = "RequireWriteScope")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(BalanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<BalanceResponse>> Withdraw([FromBody] WithdrawRequest request)
    {
        var res = await service.WithdrawAsync(request);
        return this.ToActionResult(res, balance => new BalanceResponse(balance));
    }

    // 送金: 単一トランザクションで出金+入金+MoneyLog2件。成功時は送金元の新残高を返す。
    [HttpPost("transfer")]
    [Authorize(Policy = "RequireWriteScope")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(BalanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<BalanceResponse>> Transfer([FromBody] TransferRequest request)
    {
        var res = await service.TransferAsync(request);
        return this.ToActionResult(res, balance => new BalanceResponse(balance));
    }
}
