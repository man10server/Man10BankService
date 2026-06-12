using Man10BankService.Models.Requests;
using Man10BankService.Models.Responses;
using Man10BankService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LoanController(LoanService service) : ControllerBase
{
    [HttpGet("borrower/{uuid}")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(List<LoanResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<LoanResponse>>> GetByBorrower([FromRoute] string uuid, [FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var res = await service.GetByBorrowerUuidAsync(uuid, limit, offset);
        return this.ToActionResult(res, list => list.Select(LoanResponse.From).ToList());
    }

    [HttpPost]
    [Authorize(Policy = "RequireWriteScope")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(LoanResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<LoanResponse>> Create([FromBody] LoanCreateRequest request)
    {
        var res = await service.CreateAsync(request);
        if (!res.IsSuccess)
            return this.BuildErrorResult(res.Code);

        var dto = LoanResponse.From(res.Data!);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpGet("{id:int}")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(LoanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<LoanResponse>> GetById([FromRoute] int id)
    {
        var res = await service.GetByIdAsync(id);
        return this.ToActionResult(res, LoanResponse.From);
    }

    [HttpPost("{id:int}/repay")]
    [Authorize(Policy = "RequireWriteScope")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(LoanRepayResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<LoanRepayResponse>> Repay([FromRoute] int id, [FromQuery] string collectorUuid)
    {
        var res = await service.RepayAsync(id, collectorUuid);
        return this.ToActionResult(res);
    }

    [HttpPost("{id:int}/collateral/release")]
    [Authorize(Policy = "RequireWriteScope")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(LoanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<LoanResponse>> ReleaseCollateral([FromRoute] int id, [FromQuery] string borrowerUuid)
    {
        var res = await service.ReleaseCollateralAsync(id, borrowerUuid);
        return this.ToActionResult(res, e => LoanResponse.From(e!));
    }
}
