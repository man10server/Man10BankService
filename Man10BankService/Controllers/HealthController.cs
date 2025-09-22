using Man10BankService.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Man10BankService.Models.Responses;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController(IDbContextFactory<BankDbContext> dbFactory) : ControllerBase
{

    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType(typeof(HealthPayload), StatusCodes.Status200OK)]
    public async Task<ActionResult<HealthPayload>> Get()
    {
        var nowUtc = DateTime.UtcNow;
        var proc = System.Diagnostics.Process.GetCurrentProcess();
        var startedUtc = proc.StartTime.ToUniversalTime();
        var uptimeSec = (long)(nowUtc - startedUtc).TotalSeconds;

        bool dbOk;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            dbOk = await db.Database.CanConnectAsync();
        }
        catch
        {
            dbOk = false;
        }

        var payload = new HealthPayload(
            Service: "Man10BankService",
            ServerTimeUtc: nowUtc,
            StartedAtUtc: startedUtc,
            UptimeSeconds: uptimeSec,
            Database: dbOk
        );

        return Ok(payload);
    }
}
