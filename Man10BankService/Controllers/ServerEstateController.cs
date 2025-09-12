using Man10BankService.Services;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ServerEstateController(ServerEstateService service) : ControllerBase
{
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var res = await service.GetHistoryAsync(limit, offset);
        return StatusCode(res.StatusCode, res);
    }
}

