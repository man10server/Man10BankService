using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Models.Responses;
using Man10BankService.Services;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LoanController(LoanService service) : ControllerBase
{
    [HttpGet("borrower/{uuid}")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(List<Loan>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<Loan>>> GetByBorrower([FromRoute] string uuid, [FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var res = await service.GetByBorrowerUuidAsync(uuid, limit, offset);
        return this.ToActionResult(res);
    }

    [HttpPost]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(Loan), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Loan?>> Create([FromBody] LoanCreateRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var res = await service.CreateAsync(request);
        return this.ToActionResult(res);
    }

    [HttpGet("{id:int}")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(Loan), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Loan>> GetById([FromRoute] int id)
    {
        var res = await service.GetByIdAsync(id);
        return this.ToActionResult(res);
    }

    [HttpPost("{id:int}/repay")] 
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
    [Produces("application/json")]
    [ProducesResponseType(typeof(Loan), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Loan?>> ReleaseCollateral([FromRoute] int id, [FromQuery] string borrowerUuid)
    {
        var res = await service.ReleaseCollateralAsync(id, borrowerUuid);
        return this.ToActionResult(res);
    }
}
