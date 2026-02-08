using Man10BankService.Services;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MinecraftProfileController : ControllerBase
{
    public sealed record UuidResolveResponse(string Uuid);

    [HttpGet("uuid")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(UuidResolveResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<UuidResolveResponse>> GetUuid([FromQuery] string minecraftId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(minecraftId))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "MinecraftIDを指定してください。",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var uuid = await MinecraftProfileService.GetUuidByNameAsync(minecraftId, ct);
        if (string.IsNullOrWhiteSpace(uuid))
        {
            return NotFound(new ProblemDetails
            {
                Title = "指定したMinecraftIDが見つかりません。",
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(new UuidResolveResponse(uuid));
    }
}
