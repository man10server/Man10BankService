using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BankController(BankService service) : ControllerBase
{
    [HttpGet("{uuid}/balance")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(decimal), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<decimal>> GetBalance([FromRoute] string uuid)
    {
        var res = await service.GetBalanceAsync(uuid);
        return res.StatusCode == 200 ? Ok(res.Data) : this.ToProblem(res);
    }

    [HttpGet("{uuid}/logs")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(List<MoneyLog>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<MoneyLog>>> GetLogs([FromRoute] string uuid, [FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var res = await service.GetLogsAsync(uuid, limit, offset);
        return res.StatusCode == 200 ? Ok(res.Data) : this.ToProblem(res);
    }

    [HttpPost("deposit")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(decimal), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<decimal>> Deposit([FromBody] DepositRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var res = await service.DepositAsync(request);
        return res.StatusCode == 200 ? Ok(res.Data) : this.ToProblem(res);
    }

    [HttpPost("withdraw")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(decimal), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<decimal>> Withdraw([FromBody] WithdrawRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var res = await service.WithdrawAsync(request);
        return res.StatusCode == 200 ? Ok(res.Data) : this.ToProblem(res);
    }
}
