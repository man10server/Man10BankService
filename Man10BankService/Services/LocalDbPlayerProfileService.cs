using System.Collections.Concurrent;
using Man10BankService.Data;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

// uuid→プレイヤー名をローカルDB(既存の非正規化行)から解決する実装。
// 旧 MojangPlayerProfileService(外部 sessionserver.mojang.com 呼び出し)の置き換え。
//
// 動機: 名前解決は全書き込みの先頭で呼ばれ、BankService の直列化キュー(単一ワーカー)内かつ
//   トランザクションを開いたまま実行される。そこに外部HTTP(レート制限/最大8秒)が乗ると、
//   連打負荷でレイテンシが積み上がり(17秒級)、Mojang のレート制限(429/timeout)が
//   null→PlayerNotFound(404)に化けて実在プレイヤーの取引まで弾かれる。ローカル解決でこれを断つ。
//
// 解決規則:
// - 不正形式の uuid は null(=PlayerNotFound 相当)。
// - 既知(user_bank / user_vault に非空 Player 行あり)ならその名前。成功結果のみ短命キャッシュする。
// - well-formed だがローカル未記録なら空文字を返す。口座作成系は PlayerNotFound で失敗せず継続でき、
//   かつ GetOrCreate/ChangeBalanceCore は「空名は上書きしない」ため既存の正しい名前を壊さない
//   (新規口座のみ一時的に空名。Mojang 廃止に伴う既知の許容点)。
public sealed class LocalDbPlayerProfileService(IDbContextFactory<BankDbContext> dbFactory) : IPlayerProfileService
{
    // 成功(非空名)のみキャッシュする。未知/空はキャッシュせず、名前が現れ次第拾えるようにする。
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<string?> GetNameByUuidAsync(string uuid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uuid))
            return null;

        // 形式チェック(ダッシュ有無を許容)。不正形式は PlayerNotFound 相当の null。
        var normalized = uuid.Replace("-", string.Empty).Trim();
        if (normalized.Length != 32 || !IsHex(normalized))
            return null;

        if (_cache.TryGetValue(uuid, out var cached))
            return cached;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // 非ロックの SELECT(名前は user_bank / user_vault に非正規化されている)。
        var name = await db.UserBanks.AsNoTracking()
                       .Where(x => x.Uuid == uuid && x.Player != "")
                       .Select(x => x.Player)
                       .FirstOrDefaultAsync(ct)
                   ?? await db.UserVaults.AsNoTracking()
                       .Where(x => x.Uuid == uuid && x.Player != "")
                       .Select(x => x.Player)
                       .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrWhiteSpace(name))
        {
            _cache[uuid] = name;
            return name;
        }

        // well-formed だが未記録: 空文字で継続(口座作成を妨げず、既存名も壊さない)。
        return string.Empty;
    }

    private static bool IsHex(string s)
    {
        foreach (var c in s)
        {
            var isHex = c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
            if (!isHex) return false;
        }

        return true;
    }
}
