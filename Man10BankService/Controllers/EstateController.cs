using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]/{uuid}")]
public class EstateController(EstateService service) : ControllerBase
{
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType(typeof(Estate), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Estate>> GetLatest([FromRoute] string uuid)
    {
        var res = await service.GetLatestAsync(uuid);
        if (res.StatusCode == 200) return Ok(res.Data);
        return ToProblem(res);
    }

    [HttpGet("history")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(List<EstateHistory>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<EstateHistory>>> GetHistory([FromRoute] string uuid, [FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var res = await service.GetHistoryAsync(uuid, limit, offset);
        if (res.StatusCode == 200) return Ok(res.Data);
        return ToProblem(res);
    }

    [HttpPost("snapshot")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<bool>> UpdateSnapshot([FromRoute] string uuid, [FromBody] EstateUpdateRequest request)
    {
        var res = await service.UpdateSnapshotAsync(uuid, request);
        if (res.StatusCode == 200) return Ok(res.Data);
        return ToProblem(res);
    }

    [HttpGet("~/api/[controller]/ranking")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(List<Estate>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<Estate>>> GetRanking([FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var res = await service.GetRankingAsync(limit, offset);
        if (res.StatusCode == 200) return Ok(res.Data);
        return ToProblem(res);
    }

    private ActionResult ToProblem<T>(ApiResult<T> res)
    {
        var pd = new ProblemDetails { Title = res.Code.ToString(), Status = res.StatusCode };
        pd.Extensions["code"] = res.Code.ToString();
        return StatusCode(res.StatusCode, pd);
    }
}
