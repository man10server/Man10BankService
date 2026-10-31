using Man10BankService.Data;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

// プレイヤー名簿(player_profile)の登録と参照。
// プラグインがログイン時に UUID と Minecraft ID を送り、ここで登録/更新する(改名の追従)。
// 金銭操作の直列化ワーカーには載せない(ログインのたびに呼ばれるため、ワーカーの負荷にしない)。
public class PlayerRegistryService(IDbContextFactory<BankDbContext> dbFactory)
{
    private const int MaxNameLength = 16;

    public async Task<ApiResult<PlayerProfile>> RegisterAsync(PlayerRegisterRequest req)
    {
        var uuid = req.Uuid.Trim().ToLowerInvariant();
        var name = req.Name.Trim();
        if (name.Length is 0 or > MaxNameLength)
            return ApiResult<PlayerProfile>.Fail(ErrorCode.ValidationError);

        try
        {
            // 同じ UUID が別サーバーから同時に登録されると主キー重複になりうるので、一度だけ更新として再試行する。
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                var profile = await db.PlayerProfiles.FirstOrDefaultAsync(x => x.Uuid == uuid);
                if (profile == null)
                {
                    profile = new PlayerProfile { Uuid = uuid, Name = name, UpdatedAt = DateTime.UtcNow };
                    db.PlayerProfiles.Add(profile);
                }
                else
                {
                    profile.Name = name;
                    profile.UpdatedAt = DateTime.UtcNow;
                }

                try
                {
                    await db.SaveChangesAsync();
                    return ApiResult<PlayerProfile>.Ok(profile);
                }
                catch (DbUpdateException) when (attempt == 0)
                {
                    // 同時登録による主キー重複。次の試行では既存行の更新になる。
                }
            }
            return ApiResult<PlayerProfile>.Fail(ErrorCode.Conflict);
        }
        catch (Exception)
        {
            return ApiResult<PlayerProfile>.Fail(ErrorCode.UnexpectedError);
        }
    }

    public async Task<ApiResult<PlayerProfile>> GetByUuidAsync(string uuid)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var normalized = uuid.Trim().ToLowerInvariant();
            var profile = await db.PlayerProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.Uuid == normalized);
            return profile == null
                ? ApiResult<PlayerProfile>.Fail(ErrorCode.PlayerNotFound)
                : ApiResult<PlayerProfile>.Ok(profile);
        }
        catch (Exception)
        {
            return ApiResult<PlayerProfile>.Fail(ErrorCode.UnexpectedError);
        }
    }

    // 名前から UUID を解決する。名前は改名後に別人が取得しうるため、最後にその名前でログインした人を返す。
    public async Task<ApiResult<PlayerProfile>> GetByNameAsync(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is 0 or > MaxNameLength)
            return ApiResult<PlayerProfile>.Fail(ErrorCode.ValidationError);

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var lower = trimmed.ToLowerInvariant();
            var profile = await db.PlayerProfiles
                .AsNoTracking()
                .Where(x => x.Name.ToLower() == lower)
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync();
            return profile == null
                ? ApiResult<PlayerProfile>.Fail(ErrorCode.PlayerNotFound)
                : ApiResult<PlayerProfile>.Ok(profile);
        }
        catch (Exception)
        {
            return ApiResult<PlayerProfile>.Fail(ErrorCode.UnexpectedError);
        }
    }
}
