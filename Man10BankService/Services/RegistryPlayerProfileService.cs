using Man10BankService.Data;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

// UUID からプレイヤー名を解決する。ログイン時に登録された名簿(player_profile)を最優先し、
// 未登録なら口座(user_bank)の名前で補う。外部 API(Mojang)には問い合わせない:
// 外部 API の遅延・障害が金銭操作の失敗やタイムアウトに波及するのを防ぐため。
// この鯖群に一度もログインしておらず口座も無い UUID は解決できず null(=PlayerNotFound)になる。
public sealed class RegistryPlayerProfileService(IDbContextFactory<BankDbContext> dbFactory) : IPlayerProfileService
{
    public async Task<string?> GetNameByUuidAsync(string uuid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uuid))
            return null;

        var normalized = uuid.Trim().ToLowerInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var registered = await db.PlayerProfiles
            .AsNoTracking()
            .Where(x => x.Uuid == normalized)
            .Select(x => x.Name)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(registered))
            return registered;

        var stored = await db.UserBanks
            .AsNoTracking()
            .Where(x => x.Uuid == normalized)
            .Select(x => x.Player)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(stored) ? null : stored;
    }
}
