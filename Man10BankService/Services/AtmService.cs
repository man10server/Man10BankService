using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Repositories;
using Man10BankService.Data;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

public class AtmService(IDbContextFactory<BankDbContext> dbFactory, IPlayerProfileService profileService)
{
    public async Task<ApiResult<AtmLog>> AddLogAsync(AtmLogRequest req)
    {
        try
        {
            var repo = new AtmRepository(dbFactory);
            var player = await profileService.GetNameByUuidAsync(req.Uuid);
            if (player == null)
            {
                return ApiResult<AtmLog>.NotFound(ErrorCode.PlayerNotFound);
            }
            var log = await repo.AddAtmLogAsync(req.Uuid, player, req.Amount, req.Deposit);
            return ApiResult<AtmLog>.Ok(log);
        }
        catch (ArgumentException)
        {
            return ApiResult<AtmLog>.BadRequest(ErrorCode.ValidationError);
        }
        catch (Exception)
        {
            return ApiResult<AtmLog>.Error(ErrorCode.UnexpectedError);
        }
    }

    public async Task<ApiResult<List<AtmLog>>> GetLogsAsync(string uuid, int limit = 100, int offset = 0)
    {
        if (limit is < 1 or > 1000)
            return ApiResult<List<AtmLog>>.BadRequest(ErrorCode.LimitOutOfRange);
        if (offset < 0)
            return ApiResult<List<AtmLog>>.BadRequest(ErrorCode.OffsetOutOfRange);
        try
        {
            var repo = new AtmRepository(dbFactory);
            var logs = await repo.GetAtmLogsAsync(uuid, limit, offset);
            return ApiResult<List<AtmLog>>.Ok(logs);
        }
        catch (Exception)
        {
            return ApiResult<List<AtmLog>>.Error(ErrorCode.UnexpectedError);
        }
}
}
