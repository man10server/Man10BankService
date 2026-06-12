using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Man10BankService.Models.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]/{uuid}")]
public class ServerLoanController(ServerLoanService service) : ControllerBase
{

    [HttpGet("")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ServerLoan), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ServerLoan>> GetByUuid([FromRoute] string uuid)
    {
        var res = await service.GetByUuidAsync(uuid);
        return this.ToActionResult(res);
    }

    [HttpPost("borrow")]
    [Authorize(Policy = "RequireWriteScope")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ServerLoan), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ServerLoan?>> Borrow([FromRoute] string uuid, [FromBody] ServerLoanBorrowBodyRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var res = await service.BorrowAsync(uuid, request.Amount);
        return this.ToActionResult(res);
    }

    [HttpPost("repay")]
    [Authorize(Policy = "RequireWriteScope")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ServerLoan), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ServerLoan?>> Repay([FromRoute] string uuid, [FromQuery] decimal? amount)
    {
        var res = await service.RepayAsync(uuid, amount);
        return this.ToActionResult(res);
    }

    [HttpPost("payment-amount")]
    [Authorize(Policy = "RequireWriteScope")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ServerLoan), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ServerLoan?>> SetPaymentAmount([FromRoute] string uuid, [FromQuery] decimal paymentAmount)
    {
        var res = await service.SetPaymentAmountAsync(uuid, paymentAmount);
        return this.ToActionResult(res);
    }

    [HttpPost("borrow-amount")]
    [Authorize(Policy = "RequireWriteScope")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ServerLoan), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ServerLoan?>> SetBorrowAmount([FromRoute] string uuid, [FromQuery, BindRequired] decimal amount)
    {
        var res = await service.SetBorrowAmountAsync(uuid, amount);
        return this.ToActionResult(res);
    }

    [HttpGet("borrow-limit")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(decimal), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<decimal>> GetBorrowLimit([FromRoute] string uuid)
    {
        var res = await service.CalculateBorrowLimitAsync(uuid);
        return this.ToActionResult(res);
    }

    [HttpGet("logs")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(List<ServerLoanLog>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<ServerLoanLog>>> GetLogs([FromRoute] string uuid, [FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var res = await service.GetLogsAsync(uuid, limit, offset);
        return this.ToActionResult(res);
    }

    [HttpGet("payment-info")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(PaymentInfoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PaymentInfoResponse>> GetPaymentInfo([FromRoute] string uuid)
    {
        var res = await service.GetPaymentInfoAsync(uuid);
        return this.ToActionResult(res);
    }
}
