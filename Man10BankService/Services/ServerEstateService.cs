using Man10BankService.Data;
using Man10BankService.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

public class ServerEstateService
{
    private readonly IDbContextFactory<BankDbContext> _dbFactory;

    public ServerEstateService(IDbContextFactory<BankDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
        Task.Run(SchedulerLoopAsync);
    }

    private async Task SchedulerLoopAsync()
    {
        DateTime? lastRunHourUtc = null;
        while (true)
        {
            try
            {
                var nowUtc = DateTime.UtcNow;
                var currentHourUtc = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, DateTimeKind.Utc);
                if (lastRunHourUtc != currentHourUtc)
                {
                    await RecordSnapshotAsync(currentHourUtc);
                    lastRunHourUtc = currentHourUtc;
                }
            }
            catch
            {
                // ignore
            }

            try { await Task.Delay(TimeSpan.FromMinutes(1)); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task RecordSnapshotAsync(DateTime hourUtc)
    {
        var repo = new ServerEstateRepository(_dbFactory);
        await repo.RecordSnapshotAsync(hourUtc);
    }
}
