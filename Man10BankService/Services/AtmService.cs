using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Repositories;
using Man10BankService.Data;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

public class AtmService(IDbContextFactory<BankDbContext> dbFactory)
{
    public async Task<ApiResult<AtmLog>> AddLogAsync(AtmLogRequest req)
    {
        try
        {
            var repo = new AtmRepository(dbFactory);
            var player = await MinecraftProfileService.GetNameByUuidAsync(req.Uuid) ?? string.Empty;
            var log = await repo.AddAtmLogAsync(req.Uuid, player, req.Amount, req.Deposit);
            return ApiResult<AtmLog>.Ok(log);
        }
        catch (ArgumentException ex)
        {
            return ApiResult<AtmLog>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResult<AtmLog>.Error($"ATMログの追加に失敗しました: {ex.Message}");
        }
    }

    public async Task<ApiResult<List<AtmLog>>> GetLogsAsync(string uuid, int limit = 100, int offset = 0)
    {
        if (limit is < 1 or > 1000)
            return ApiResult<List<AtmLog>>.BadRequest("limit は 1..1000 の範囲で指定してください。");
        if (offset < 0)
            return ApiResult<List<AtmLog>>.BadRequest("offset は 0 以上で指定してください。");
        try
        {
            var repo = new AtmRepository(dbFactory);
            var logs = await repo.GetAtmLogsAsync(uuid, limit, offset);
            return ApiResult<List<AtmLog>>.Ok(logs);
        }
        catch (Exception ex)
        {
            return ApiResult<List<AtmLog>>.Error($"ATMログの取得に失敗しました: {ex.Message}");
        }
    }
}
