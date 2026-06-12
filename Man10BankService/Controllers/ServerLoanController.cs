using System.ComponentModel.DataAnnotations;
using Man10BankService.Models.Requests;
using Man10BankService.Models.Responses;
using Man10BankService.Services;
using Man10BankService.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]/{uuid:uuid}")]
public class ServerLoanController(ServerLoanService service) : ControllerBase
{

    [HttpGet("")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ServerLoanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ServerLoanResponse>> GetByUuid([FromRoute] string uuid)
    {
        var res = await service.GetByUuidAsync(uuid);
        return this.ToActionResult(res, ServerLoanResponse.From);
    }

    [HttpPost("borrow")]
    [Authorize(Policy = "RequireWriteScope")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ServerLoanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ServerLoanResponse>> Borrow([FromRoute] string uuid, [FromBody] ServerLoanBorrowBodyRequest request)
    {
        var res = await service.BorrowAsync(uuid, request.Amount);
        return this.ToActionResult(res, e => ServerLoanResponse.From(e!));
    }

    [HttpPost("repay")]
    [Authorize(Policy = "RequireWriteScope")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ServerLoanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ServerLoanResponse>> Repay(
        [FromRoute] string uuid,
        [FromQuery, Range(typeof(decimal), AmountLimits.MinText, AmountLimits.MaxText, ErrorMessage = "金額は 0 以上 1 兆以下で指定してください。")] decimal? amount)
    {
        var res = await service.RepayAsync(uuid, amount);
        return this.ToActionResult(res, e => ServerLoanResponse.From(e!));
    }

    [HttpPost("payment-amount")]
    [Authorize(Policy = "RequireWriteScope")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ServerLoanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ServerLoanResponse>> SetPaymentAmount(
        [FromRoute] string uuid,
        [FromQuery, BindRequired, Range(typeof(decimal), AmountLimits.MinText, AmountLimits.MaxText, ErrorMessage = "金額は 0 以上 1 兆以下で指定してください。")] decimal paymentAmount)
    {
        var res = await service.SetPaymentAmountAsync(uuid, paymentAmount);
        return this.ToActionResult(res, e => ServerLoanResponse.From(e!));
    }

    [HttpPost("borrow-amount")]
    [Authorize(Policy = "RequireWriteScope")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ServerLoanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ServerLoanResponse>> SetBorrowAmount(
        [FromRoute] string uuid,
        [FromQuery, BindRequired, Range(typeof(decimal), AmountLimits.MinText, AmountLimits.MaxText, ErrorMessage = "金額は 0 以上 1 兆以下で指定してください。")] decimal amount)
    {
        var res = await service.SetBorrowAmountAsync(uuid, amount);
        return this.ToActionResult(res, e => ServerLoanResponse.From(e!));
    }

    [HttpGet("borrow-limit")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(BorrowLimitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<BorrowLimitResponse>> GetBorrowLimit([FromRoute] string uuid)
    {
        var res = await service.CalculateBorrowLimitAsync(uuid);
        return this.ToActionResult(res, limit => new BorrowLimitResponse(limit));
    }

    [HttpGet("logs")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(List<ServerLoanLogResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<ServerLoanLogResponse>>> GetLogs([FromRoute] string uuid, [FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var res = await service.GetLogsAsync(uuid, limit, offset);
        return this.ToActionResult(res, logs => logs.Select(ServerLoanLogResponse.From).ToList());
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
