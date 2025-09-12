using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChequesController(ChequeService service) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(Cheque), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Cheque>> Create([FromBody] ChequeCreateRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var res = await service.CreateAsync(request);
        if (res.StatusCode == 200) return Ok(res.Data);
        return ToProblem(res);
    }

    [HttpGet("{id:int}")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(Cheque), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Cheque>> Get([FromRoute] int id)
    {
        var res = await service.GetAsync(id);
        if (res.StatusCode == 200) return Ok(res.Data);
        return ToProblem(res);
    }

    [HttpPost("{id:int}/use")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(Cheque), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Cheque>> Use([FromRoute] int id, [FromBody] ChequeUseRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var res = await service.UseAsync(id, request);
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
