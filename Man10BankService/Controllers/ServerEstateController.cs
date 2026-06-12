using Man10BankService.Models.Responses;
using Man10BankService.Services;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ServerEstateController(ServerEstateService service) : ControllerBase
{
    [HttpGet("history")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(List<ServerEstateHistoryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<ServerEstateHistoryResponse>>> GetHistory([FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var res = await service.GetHistoryAsync(limit, offset);
        return this.ToActionResult(res, list => list.Select(ServerEstateHistoryResponse.From).ToList());
    }
}
