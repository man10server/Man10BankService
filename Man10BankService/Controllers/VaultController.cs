using Man10BankService.Hubs;
using Man10BankService.Models.Requests;
using Man10BankService.Models.Responses;
using Man10BankService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

// 電子マネー(user_vault)の REST API。BankController に倣う。書き込みは RequireWriteScope。
// 各書き込みは確定後に VaultWsHub が在席サーバーへ targeting push する(VaultProvider 7.2)。
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

    [HttpPost("deposit")]
    [Authorize(Policy = "RequireWriteScope")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(VaultBalanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
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

    // 電子マネー → 電子マネー送金(/pay)。送受信者が同一サーバー在席時のみ呼ばれる前提(プラグイン層で判定)。
    [HttpPost("transfer")]
    [Authorize(Policy = "RequireWriteScope")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(VaultBalanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<VaultBalanceResponse>> Transfer([FromBody] VaultTransferRequest request)
    {
        var res = await service.TransferAsync(request);
        return this.ToActionResult(res);
    }

    // 管理用: 絶対値設定。オフライン残高を変更できる唯一の経路(VaultProvider 4.5)。
    [HttpPost("set")]
    [Authorize(Policy = "RequireWriteScope")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(VaultBalanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
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

    // push + presence の双方向 WebSocket チャネル(VaultProvider 5)。
    // 認証必須(FallbackPolicy)。各 Paper サーバーは起動時に 1 本張る。
    // server: 接続元サーバー名(presence 失効/診断用)。
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
