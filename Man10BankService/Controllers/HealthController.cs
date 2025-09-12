using Man10BankService.Data;
using Man10BankService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController(IDbContextFactory<BankDbContext> dbFactory) : ControllerBase
{
    private sealed record HealthPayload(
        string Service,
        DateTime ServerTimeUtc,
        DateTime StartedAtUtc,
        long UptimeSeconds,
        bool Database
    );

    [HttpGet]
    public async Task<IActionResult> Get()
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

        // サービス自体は起動しているため 200 固定。DB可否は payload の Database.Connected を参照。
        return StatusCode(200, ApiResult<HealthPayload>.Ok(payload));
    }
}

