using Man10BankService.Models.Database;
using Man10BankService.Services;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ServerEstateController(ServerEstateService service) : ControllerBase
{
    [HttpGet("history")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(List<ServerEstateHistory>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<ServerEstateHistory>>> GetHistory([FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var res = await service.GetHistoryAsync(limit, offset);
        return res.StatusCode == 200 ? Ok(res.Data) : this.ToProblem(res);
    }
}
