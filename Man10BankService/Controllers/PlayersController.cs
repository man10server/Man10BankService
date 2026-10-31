using Man10BankService.Models.Requests;
using Man10BankService.Models.Responses;
using Man10BankService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PlayersController(PlayerRegistryService service) : ControllerBase
{
    // ログイン時にプラグインから呼ばれる。UUID と Minecraft ID を登録し、既存なら名前を更新する(改名の反映)。
    [HttpPost]
    [Authorize(Policy = "RequireWriteScope")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(PlayerProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PlayerProfileResponse>> Register([FromBody] PlayerRegisterRequest request)
    {
        var res = await service.RegisterAsync(request);
        return this.ToActionResult(res, PlayerProfileResponse.From);
    }

    [HttpGet("{uuid:uuid}")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(PlayerProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PlayerProfileResponse>> GetByUuid([FromRoute] string uuid)
    {
        var res = await service.GetByUuidAsync(uuid);
        return this.ToActionResult(res, PlayerProfileResponse.From);
    }

    // 名前から UUID を解決する(/mpay の送金先解決など)。この鯖群にログインしたことがある名前のみ解決できる。
    [HttpGet("by-name/{name}")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(PlayerProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PlayerProfileResponse>> GetByName([FromRoute] string name)
    {
        var res = await service.GetByNameAsync(name);
        return this.ToActionResult(res, PlayerProfileResponse.From);
    }
}
