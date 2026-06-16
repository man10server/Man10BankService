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
    }

    public async Task<ApiResult<List<ServerEstateHistory>>> GetHistoryAsync(int limit = 100, int offset = 0)
    {
        if (limit is < 1 or > 1000) return ApiResult<List<ServerEstateHistory>>.Fail(ErrorCode.LimitOutOfRange);
        if (offset < 0) return ApiResult<List<ServerEstateHistory>>.Fail(ErrorCode.OffsetOutOfRange);
        try
        {
            var repo = new ServerEstateRepository(_dbFactory);
            var list = await repo.GetHistoryAsync(limit, offset);
            return ApiResult<List<ServerEstateHistory>>.Ok(list);
        }
        catch (Exception)
        {
            return ApiResult<List<ServerEstateHistory>>.Fail(ErrorCode.UnexpectedError);
        }
    }

    // 指定時刻(時単位)のスナップショットが既に記録済みか(冪等判定に使用)
    public async Task<bool> HasSnapshotForHourAsync(DateTime hourUtc)
    {
        var repo = new ServerEstateRepository(_dbFactory);
        return await repo.HasSnapshotForHourAsync(hourUtc);
    }

    // 指定時刻(時単位)のサーバー資産スナップショットを記録する（スケジューラから呼ばれる）。
    // リポジトリ側でも同一時刻の重複INSERTを抑止する(冪等化)。
    public async Task RecordSnapshotAsync(DateTime hourUtc)
    {
        var repo = new ServerEstateRepository(_dbFactory);
        await repo.RecordSnapshotAsync(hourUtc);
    }
}
