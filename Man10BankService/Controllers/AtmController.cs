using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AtmController(AtmService service) : ControllerBase
{
    [HttpGet("{uuid}/logs")]
    public async Task<IActionResult> GetLogs([FromRoute] string uuid, [FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var res = await service.GetLogsAsync(uuid, limit, offset);
        return StatusCode(res.StatusCode, res);
    }

    [HttpPost("logs")]
    public async Task<IActionResult> AddLog([FromBody] AtmLogRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var res = await service.AddLogAsync(request);
        return StatusCode(res.StatusCode, res);
    }
}
