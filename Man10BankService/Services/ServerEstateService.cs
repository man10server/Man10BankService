using Man10BankService.Data;
using Man10BankService.Models.Database;
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

    public async Task<ApiResult<List<ServerEstateHistory>>> GetHistoryAsync(int limit = 100, int offset = 0)
    {
        if (limit is < 1 or > 1000) return ApiResult<List<ServerEstateHistory>>.BadRequest(ErrorCode.LimitOutOfRange);
        if (offset < 0) return ApiResult<List<ServerEstateHistory>>.BadRequest(ErrorCode.OffsetOutOfRange);
        try
        {
            var repo = new ServerEstateRepository(_dbFactory);
            var list = await repo.GetHistoryAsync(limit, offset);
            return ApiResult<List<ServerEstateHistory>>.Ok(list);
        }
        catch (Exception)
        {
            return ApiResult<List<ServerEstateHistory>>.Error(ErrorCode.UnexpectedError);
        }
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
                    var repo = new ServerEstateRepository(_dbFactory);
                    await repo.RecordSnapshotAsync(currentHourUtc);
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
}
