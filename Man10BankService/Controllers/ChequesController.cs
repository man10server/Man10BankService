using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChequesController(ChequeService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ChequeCreateRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var res = await service.CreateAsync(request);
        return StatusCode(res.StatusCode, res);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get([FromRoute] int id)
    {
        var res = await service.GetAsync(id);
        return StatusCode(res.StatusCode, res);
    }

    [HttpPost("{id:int}/use")]
    public async Task<IActionResult> Use([FromRoute] int id, [FromBody] ChequeUseRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var res = await service.UseAsync(id, request);
        return StatusCode(res.StatusCode, res);
    }
}
