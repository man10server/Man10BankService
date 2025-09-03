using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LoanController(LoanService service) : ControllerBase
{
    [HttpGet("borrower/{uuid}")]
    public async Task<IActionResult> GetByBorrower([FromRoute] string uuid, [FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var res = await service.GetByBorrowerUuidAsync(uuid, limit, offset);
        return StatusCode(res.StatusCode, res);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LoanCreateRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var res = await service.CreateAsync(request);
        return StatusCode(res.StatusCode, res);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById([FromRoute] int id)
    {
        var res = await service.GetByIdAsync(id);
        return StatusCode(res.StatusCode, res);
    }

    [HttpPost("{id:int}/repay")] 
    public async Task<IActionResult> Repay([FromRoute] int id, [FromQuery] string collectorUuid)
    {
        if (string.IsNullOrWhiteSpace(collectorUuid))
            return BadRequest("collectorUuid を指定してください。");
        var res = await service.RepayAsync(id, collectorUuid);
        return StatusCode(res.StatusCode, res);
    }

    [HttpPost("{id:int}/collateral/release")] 
    public async Task<IActionResult> ReleaseCollateral([FromRoute] int id, [FromQuery] string borrowerUuid)
    {
        if (string.IsNullOrWhiteSpace(borrowerUuid))
            return BadRequest("borrowerUuid を指定してください。");
        var res = await service.ReleaseCollateralAsync(id, borrowerUuid);
        return StatusCode(res.StatusCode, res);
    }
}
