using Man10BankService.Models.Requests;
using Man10BankService.Models.Responses;
using Man10BankService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AtmController(AtmService service) : ControllerBase
{
    [HttpGet("{uuid}/logs")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(List<AtmLogResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<AtmLogResponse>>> GetLogs([FromRoute] string uuid, [FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var res = await service.GetLogsAsync(uuid, limit, offset);
        return this.ToActionResult(res, logs => logs.Select(AtmLogResponse.From).ToList());
    }

    [HttpPost("logs")]
    [Authorize(Policy = "RequireWriteScope")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(AtmLogResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<AtmLogResponse>> AddLog([FromBody] AtmLogRequest request)
    {
        var res = await service.AddLogAsync(request);
        if (!res.IsSuccess)
            return this.BuildErrorResult(res.Code);

        var dto = AtmLogResponse.From(res.Data!);
        // 単一取得エンドポイントが無いため、当該プレイヤーのログ一覧を Location とする
        return CreatedAtAction(nameof(GetLogs), new { uuid = dto.Uuid }, dto);
    }
}
