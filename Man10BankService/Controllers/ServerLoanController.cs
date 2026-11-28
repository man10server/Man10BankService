using System.ComponentModel.DataAnnotations;
using Man10BankService.Models.Requests;
using Man10BankService.Models.Responses;
using Man10BankService.Services;
using Man10BankService.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Man10BankService.Controllers;

// サーバーローン(リボ)の API。{uuid} は操作するプレイヤー。
// 他のプレイヤーの契約に参加しているアカウントを指定すると、参加先の契約に対して操作する。
[ApiController]
[Route("api/[controller]/{uuid:uuid}")]
public class ServerLoanController(ServerLoanService service) : ControllerBase
{
    private const string UuidPattern = @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";
    private const string UuidErrorMessage = "UUID の形式が不正です。";

    // uuid が使っている契約(残債・支払額など)を返す。
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

    // 借入。借りたお金は uuid の銀行口座に入る。
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
        return this.ToActionResult(res, ServerLoanResponse.From);
    }

    // 手動返済。uuid の銀行口座から amount を返す(残債を超える分は返さない)。
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
        [FromQuery, BindRequired, Range(typeof(decimal), AmountLimits.MinText, AmountLimits.MaxText, ErrorMessage = "金額は 0 以上 1 兆以下で指定してください。")] decimal amount)
    {
        var res = await service.RepayAsync(uuid, amount);
        return this.ToActionResult(res, ServerLoanResponse.From);
    }

    // 支払額(設定)の変更。定期返済で引き落としてほしい額を決める。
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
        return this.ToActionResult(res, ServerLoanResponse.From);
    }

    // 運営用: 残債を amount に上書きする。
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
        return this.ToActionResult(res, ServerLoanResponse.From);
    }

    // 借入枠(残債の上限)。
    [HttpGet("borrow-limit")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(BorrowLimitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<BorrowLimitResponse>> GetBorrowLimit([FromRoute] string uuid)
    {
        var res = await service.CalculateBorrowLimitAsync(uuid);
        return this.ToActionResult(res, limit => new BorrowLimitResponse(limit));
    }

    // 残債があり延滞中かどうか。契約の行は作らない。
    [HttpGet("overdue")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(OverdueResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<OverdueResponse>> GetOverdue([FromRoute] string uuid)
    {
        var res = await service.HasOverdueDebtAsync(uuid);
        return this.ToActionResult(res, overdue => new OverdueResponse(overdue));
    }

    // 契約のログ(新しい順)。
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

    // 次回の定期返済の日時、1 日あたりの利息、次回の引き落とし額。
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

    // ===== 契約の共有(リンク) =====

    // uuid が使っている契約と、共有の状態(参加者・承認待ちの申請)を返す。
    [HttpGet("contract")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ServerLoanContractResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ServerLoanContractResponse>> GetContract([FromRoute] string uuid)
    {
        var res = await service.GetContractAsync(uuid);
        return this.ToActionResult(res);
    }

    // uuid が target の契約への参加を申請する。
    [HttpPost("link/request")]
    [Authorize(Policy = "RequireWriteScope")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ServerLoanContractResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ServerLoanContractResponse>> RequestLink(
        [FromRoute] string uuid,
        [FromQuery, BindRequired, RegularExpression(UuidPattern, ErrorMessage = UuidErrorMessage)] string target)
    {
        var res = await service.RequestLinkAsync(uuid, target);
        return this.ToActionResult(res);
    }

    // 本垢 uuid が、member からの参加申請を承認する。
    [HttpPost("link/approve")]
    [Authorize(Policy = "RequireWriteScope")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ServerLoanContractResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ServerLoanContractResponse>> ApproveLink(
        [FromRoute] string uuid,
        [FromQuery, BindRequired, RegularExpression(UuidPattern, ErrorMessage = UuidErrorMessage)] string member)
    {
        var res = await service.ApproveLinkAsync(uuid, member);
        return this.ToActionResult(res);
    }

    // 本垢 uuid が、member からの参加申請を拒否する。
    [HttpPost("link/reject")]
    [Authorize(Policy = "RequireWriteScope")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ServerLoanContractResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ServerLoanContractResponse>> RejectLink(
        [FromRoute] string uuid,
        [FromQuery, BindRequired, RegularExpression(UuidPattern, ErrorMessage = UuidErrorMessage)] string member)
    {
        var res = await service.RejectLinkAsync(uuid, member);
        return this.ToActionResult(res);
    }

    // uuid が、自分の出した参加申請を取り消す。
    [HttpPost("link/cancel")]
    [Authorize(Policy = "RequireWriteScope")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ServerLoanContractResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ServerLoanContractResponse>> CancelLinkRequest([FromRoute] string uuid)
    {
        var res = await service.CancelLinkRequestAsync(uuid);
        return this.ToActionResult(res);
    }

    // uuid が、参加している契約から脱退する(契約に残債がある間は不可)。
    [HttpPost("link/leave")]
    [Authorize(Policy = "RequireWriteScope")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ServerLoanContractResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ServerLoanContractResponse>> LeaveLink([FromRoute] string uuid)
    {
        var res = await service.LeaveLinkAsync(uuid);
        return this.ToActionResult(res);
    }

    // 運営用: uuid を owner の契約に、申請・承認なしで参加させる。
    // resetLimit=true を指定すると、罰則として参加先の契約の借入枠を初期枠に戻す。
    [HttpPost("link/force")]
    [Authorize(Policy = "RequireWriteScope")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ServerLoanContractResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ServerLoanContractResponse>> ForceLink(
        [FromRoute] string uuid,
        [FromQuery, BindRequired, RegularExpression(UuidPattern, ErrorMessage = UuidErrorMessage)] string owner,
        [FromQuery(Name = "operator"), BindRequired, StringLength(16, MinimumLength = 1, ErrorMessage = "operator は 1〜16 文字で指定してください。")] string operatorName,
        [FromQuery] bool resetLimit = false)
    {
        var res = await service.ForceLinkAsync(uuid, owner, operatorName, resetLimit);
        return this.ToActionResult(res);
    }

    // 運営用: uuid の参加(または承認待ちの申請)を取り消す。契約に残債があっても行える。
    [HttpPost("link/remove")]
    [Authorize(Policy = "RequireWriteScope")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ServerLoanContractResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ServerLoanContractResponse>> ForceUnlink(
        [FromRoute] string uuid,
        [FromQuery(Name = "operator"), BindRequired, StringLength(16, MinimumLength = 1, ErrorMessage = "operator は 1〜16 文字で指定してください。")] string operatorName)
    {
        var res = await service.ForceUnlinkAsync(uuid, operatorName);
        return this.ToActionResult(res);
    }
}
