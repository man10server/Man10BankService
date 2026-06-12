using Man10BankService.Models.Requests;
using Man10BankService.Models.Responses;
using Man10BankService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]/{uuid}")]
public class EstateController(EstateService service) : ControllerBase
{
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType(typeof(EstateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<EstateResponse>> GetLatest([FromRoute] string uuid)
    {
        var res = await service.GetLatestAsync(uuid);
        return this.ToActionResult(res, e => EstateResponse.From(e!));
    }

    [HttpGet("history")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(List<EstateHistoryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<EstateHistoryResponse>>> GetHistory([FromRoute] string uuid, [FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var res = await service.GetHistoryAsync(uuid, limit, offset);
        return this.ToActionResult(res, list => list.Select(EstateHistoryResponse.From).ToList());
    }

    [HttpPost("snapshot")]
    [Authorize(Policy = "RequireWriteScope")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(SnapshotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SnapshotResponse>> UpdateSnapshot([FromRoute] string uuid, [FromBody] EstateUpdateRequest request)
    {
        var res = await service.UpdateSnapshotAsync(uuid, request);
        return this.ToActionResult(res, updated => new SnapshotResponse(updated));
    }

    [HttpGet("~/api/[controller]/ranking")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(List<EstateResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<EstateResponse>>> GetRanking([FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var res = await service.GetRankingAsync(limit, offset);
        return this.ToActionResult(res, list => list.Select(EstateResponse.From).ToList());
    }
}
