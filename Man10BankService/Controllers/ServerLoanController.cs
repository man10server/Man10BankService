using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Man10BankService.Models.Responses;
using Microsoft.AspNetCore.Mvc;

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
    [Produces("application/json")]
    [ProducesResponseType(typeof(ServerLoan), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ServerLoan?>> SetBorrowAmount([FromRoute] string uuid, [FromQuery] decimal amount)
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
        // ユーザーデータ取得（存在確認）
        var (statusCode, errorCode, loan) = await service.GetByUuidAsync(uuid);
        if (statusCode != StatusCodes.Status200OK || loan is null)
        {
            // 404/500 を既存の規約に合わせてそのまま返す
            return this.ToActionResult(new ApiResult<PaymentInfoResponse>(statusCode, errorCode));
        }

        // 日あたりの金利増加額（StopInterest や残債 0 は 0 とする）
        var perDay = loan.StopInterest || loan.BorrowAmount <= 0m
            ? 0m
            : ServerLoanService.CalculateDailyInterestAmount(loan.BorrowAmount);

        // 次回支払日（設定に基づく週次支払日時の次回）
        var nextRepay = ServerLoanService.GetNextWeeklyRepayDateTime();

        return Ok(new PaymentInfoResponse(nextRepay, perDay));
    }
}
