using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LoanController(LoanService service) : ControllerBase
{
    // 指定UUIDのローン一覧（債務者視点）
    [HttpGet("borrower/{uuid}")]
    public async Task<IActionResult> GetByBorrower([FromRoute] string uuid, [FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var res = await service.GetByBorrowerUuidAsync(uuid, limit, offset);
        return StatusCode(res.StatusCode, res);
    }

    // 新規契約
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LoanCreateRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var res = await service.CreateAsync(
            request.LendUuid,
            request.LendPlayer,
            request.BorrowUuid,
            request.BorrowPlayer,
            request.Amount,
            request.PaybackDate,
            request.CollateralItem
        );
        return StatusCode(res.StatusCode, res);
    }

    // 指定IDの借金データ取得
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById([FromRoute] int id)
    {
        var res = await service.GetByIdAsync(id);
        return StatusCode(res.StatusCode, res);
    }

    // 指定IDの返済（回収者UUIDを指定）
    [HttpPost("{id:int}/repay")] 
    public async Task<IActionResult> Repay([FromRoute] int id, [FromQuery] string collectorUuid)
    {
        if (string.IsNullOrWhiteSpace(collectorUuid))
            return BadRequest("collectorUuid を指定してください。");
        var res = await service.RepayAsync(id, collectorUuid);
        return StatusCode(res.StatusCode, res);
    }

    // 債務者が返済後に担保を取り返す
    [HttpPost("{id:int}/collateral/release")] 
    public async Task<IActionResult> ReleaseCollateral([FromRoute] int id, [FromQuery] string borrowerUuid)
    {
        if (string.IsNullOrWhiteSpace(borrowerUuid))
            return BadRequest("borrowerUuid を指定してください。");
        var res = await service.ReleaseCollateralAsync(id, borrowerUuid);
        return StatusCode(res.StatusCode, res);
    }
}

