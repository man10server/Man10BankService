using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]/{uuid}")]
public class EstateController(EstateService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetLatest([FromRoute] string uuid)
    {
        var res = await service.GetLatestAsync(uuid);
        return StatusCode(res.StatusCode, res);
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromRoute] string uuid, [FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var res = await service.GetHistoryAsync(uuid, limit, offset);
        return StatusCode(res.StatusCode, res);
    }

    [HttpPost("snapshot")]
    public async Task<IActionResult> UpdateSnapshot([FromRoute] string uuid, [FromBody] EstateUpdateRequest request)
    {
        var res = await service.UpdateSnapshotAsync(uuid, request);
        return StatusCode(res.StatusCode, res);
    }

    [HttpGet("~/api/[controller]/ranking")]
    public async Task<IActionResult> GetRanking([FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var res = await service.GetRankingAsync(limit, offset);
        return StatusCode(res.StatusCode, res);
    }
}
