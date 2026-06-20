using Man10BankService.Hubs;
using Man10BankService.Models.Requests;
using Man10BankService.Models.Responses;
using Man10BankService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

// 電子マネー(user_vault)用 API。書き込みは RequireWriteScope。
// オンライン状態・同一 Paper 在席の検証は呼び出し元 Paper の VaultService が行い、本 API では扱わない。
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
        return this.ToActionResult(res, d => new VaultBalanceResponse(d.Balance, d.Version));
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

    // Vault 設定取得。残高上限と Provider 移動緩和設定を配布する。
    [HttpGet("config")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(VaultConfigResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public ActionResult<VaultConfigResponse> GetConfig()
    {
        var res = service.GetConfig();
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
        return this.ToActionResult(res, d => new VaultBalanceResponse(d.Balance, d.Version));
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
        return this.ToActionResult(res, d => new VaultBalanceResponse(d.Balance, d.Version));
    }

    // 電子マネー送金(/pay)。送金元・送金先双方の更新後残高を返す。
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
        return this.ToActionResult(res, d => new VaultTransferResponse(
            d.From.Balance, d.From.Version, d.To.Balance, d.To.Version));
    }

    // user_vault <-> user_bank の移動(/deposit /withdraw)。
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
        return this.ToActionResult(res, d => new VaultMoveResponse(d.VaultBalance, d.VaultVersion, d.BankBalance));
    }

    // 管理者用の絶対値設定。在席状況を問わず受理し、衝突や制約違反はレスポンスで返す。
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
        return this.ToActionResult(res, d => new VaultBalanceResponse(d.Balance, d.Version));
    }

    // push / config 更新通知用 WebSocket。Bearer 認証必須。
    [HttpGet("ws")]
    [Authorize]
    public async Task Ws()
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        await hub.HandleConnectionAsync(socket, HttpContext.RequestAborted);
    }
}
