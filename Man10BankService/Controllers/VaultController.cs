using Man10BankService.Hubs;
using Man10BankService.Models.Requests;
using Man10BankService.Models.Responses;
using Man10BankService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

// 電子マネー(user_vault)の REST API。BankController に倣う。書き込みは RequireWriteScope。
// すべての書き込みは対象 UUID の session claim(在席サーバー+sessionId)一致を要求する(VaultProvider 8.2)。
// 残高 push は行わない。収束は claim 応答・確定応答・定期再同期で行う(VaultProvider 7.4)。
[ApiController]
[Route("api/[controller]")]
public class VaultController(VaultService service, VaultWsHub hub) : ControllerBase
{
    [HttpGet("{uuid:uuid}/balance")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(VaultBalanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<VaultBalanceResponse>> GetBalance([FromRoute] string uuid)
    {
        var res = await service.GetBalanceAsync(uuid);
        return this.ToActionResult(res);
    }

    [HttpGet("{uuid:uuid}/logs")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(List<VaultLogResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<VaultLogResponse>>> GetLogs([FromRoute] string uuid, [FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var res = await service.GetLogsAsync(uuid, limit, offset);
        return this.ToActionResult(res, logs => logs.Select(VaultLogResponse.From).ToList());
    }

    // 口座を冪等に作成する(hasAccount/createPlayerAccount 用)。残高変更は伴わない。
    [HttpPost("{uuid:uuid}/ensure")]
    [Authorize(Policy = "RequireWriteScope")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(VaultBalanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<VaultBalanceResponse>> Ensure([FromRoute] string uuid)
    {
        var res = await service.EnsureAccountAsync(uuid);
        return this.ToActionResult(res);
    }

    // join 時の session claim(後勝ち)。確定残高・version・残高上限を配布する(VaultProvider 8.1)。
    [HttpPost("session/claim")]
    [Authorize(Policy = "RequireWriteScope")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(VaultSessionClaimResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<VaultSessionClaimResponse>> ClaimSession([FromBody] VaultSessionClaimRequest request)
    {
        var res = await service.ClaimSessionAsync(request);
        return this.ToActionResult(res);
    }

    // quit 時の session release。sessionId 不一致・解除済みでも 204(冪等)。
    [HttpPost("session/release")]
    [Authorize(Policy = "RequireWriteScope")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ReleaseSession([FromBody] VaultSessionReleaseRequest request)
    {
        var res = await service.ReleaseSessionAsync(request);
        if (!res.IsSuccess)
            return this.BuildErrorResult(res.Code);
        return NoContent();
    }

    // 対象 UUID の在席サーバー / session 状態を取得する。
    [HttpGet("session/{uuid:uuid}")]
    [Authorize(Policy = "RequireWriteScope")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(VaultSessionStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public ActionResult<VaultSessionStatusResponse> GetSession([FromRoute] string uuid)
    {
        var res = service.GetSession(uuid);
        return this.ToActionResult(res);
    }

    [HttpPost("deposit")]
    [Authorize(Policy = "RequireWriteScope")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(VaultBalanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<VaultBalanceResponse>> Deposit([FromBody] VaultDepositRequest request)
    {
        var res = await service.DepositAsync(request);
        return this.ToActionResult(res);
    }

    [HttpPost("withdraw")]
    [Authorize(Policy = "RequireWriteScope")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(VaultBalanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<VaultBalanceResponse>> Withdraw([FromBody] VaultWithdrawRequest request)
    {
        var res = await service.WithdrawAsync(request);
        return this.ToActionResult(res);
    }

    // 電子マネー → 電子マネー送金(/pay)。送金元・送金先の両 claim が要求元サーバーと一致する場合のみ。
    [HttpPost("transfer")]
    [Authorize(Policy = "RequireWriteScope")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(VaultTransferResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<VaultTransferResponse>> Transfer([FromBody] VaultTransferRequest request)
    {
        var res = await service.TransferAsync(request);
        return this.ToActionResult(res);
    }

    // 管理用: 絶対値設定。他の書き込みと同様、在席サーバー(claim 一致)からのみ実行できる。
    [HttpPost("set")]
    [Authorize(Policy = "RequireWriteScope")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(VaultBalanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<VaultBalanceResponse>> Set([FromBody] VaultSetRequest request)
    {
        var res = await service.SetAsync(request);
        return this.ToActionResult(res);
    }

    // 電子マネー ⇄ 銀行残高の 1 Tx 移動(ATM/`/deposit`/`/withdraw` 用)。
    [HttpPost("move")]
    [Authorize(Policy = "RequireWriteScope")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(VaultMoveResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<VaultMoveResponse>> Move([FromBody] VaultMoveRequest request)
    {
        var res = await service.MoveAsync(request);
        return this.ToActionResult(res);
    }

    // session / presence の双方向 WebSocket チャネル(VaultProvider 7.4)。残高 push は流さない。
    // 認証必須(FallbackPolicy)。各 Paper サーバーは起動時に 1 本張り、session の寿命はこの接続に紐づく。
    // server: 接続元サーバー名(session 失効/診断用)。
    [HttpGet("ws")]
    [Authorize]
    public async Task Ws([FromQuery] string? server)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var serverName = string.IsNullOrWhiteSpace(server) ? "unknown" : server.Trim();
        using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        await hub.HandleConnectionAsync(socket, serverName, HttpContext.RequestAborted);
    }
}
