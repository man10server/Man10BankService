using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
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
        if (res.StatusCode == 200) return Ok(res.Data);
        return ToProblem(res);
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
        if (res.StatusCode == 200) return Ok(res.Data);
        return ToProblem(res);
    }

    [HttpGet("{id:int}")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(Loan), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Loan>> GetById([FromRoute] int id)
    {
        var res = await service.GetByIdAsync(id);
        if (res.StatusCode == 200) return Ok(res.Data);
        return ToProblem(res);
    }

    [HttpPost("{id:int}/repay")] 
    [Produces("application/json")]
    [ProducesResponseType(typeof(Loan), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Loan?>> Repay([FromRoute] int id, [FromQuery] string collectorUuid)
    {
        if (string.IsNullOrWhiteSpace(collectorUuid))
            return ValidationProblem(new() { Title = "collectorUuid を指定してください。" });
        var res = await service.RepayAsync(id, collectorUuid);
        if (res.StatusCode == 200) return Ok(res.Data);
        return ToProblem(res);
    }

    [HttpPost("{id:int}/collateral/release")] 
    [Produces("application/json")]
    [ProducesResponseType(typeof(Loan), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Loan?>> ReleaseCollateral([FromRoute] int id, [FromQuery] string borrowerUuid)
    {
        if (string.IsNullOrWhiteSpace(borrowerUuid))
            return ValidationProblem(new() { Title = "borrowerUuid を指定してください。" });
        var res = await service.ReleaseCollateralAsync(id, borrowerUuid);
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
