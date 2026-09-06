using System.Collections.Concurrent;

namespace Man10BankService.Services;

// プレイヤーごとの vault session(在席サーバー・sessionId)を管理するシングルトン(VaultProvider 5.8/9.3)。
// すべての vault 書き込みは claim と一致する sessionId を要求され、これが単一書き込み者を強制する。
// claim は後勝ち(last-write-wins)で、既存 session を失効させて新 session を発行する。
// 保存先はプロセス内メモリ(サービス再起動で全失効)。Paper 側は WS 再接続時に全員 claim し直す。
public sealed class VaultSessionManager
{
    public sealed record Session(string Uuid, string Player, string Server, string SessionId, string ConnectionId);

    private readonly ConcurrentDictionary<string, Session> _byUuid = new(StringComparer.OrdinalIgnoreCase);

    // 後勝ち claim。新 session を登録し、置き換えた旧 session(あれば)を返す。
    public (Session Claimed, Session? Revoked) Claim(string uuid, string player, string server, string connectionId)
    {
        var session = new Session(uuid, player, server, Guid.NewGuid().ToString("N"), connectionId);
        Session? revoked = null;
        _byUuid.AddOrUpdate(uuid, session, (_, old) =>
        {
            revoked = old;
            return session;
        });
        return (session, revoked);
    }

    // sessionId が現在の claim と一致する場合のみ解除する(古い release で後勝ちの新 session を壊さない)。
    public bool Release(string uuid, string sessionId)
    {
        if (!_byUuid.TryGetValue(uuid, out var current) || current.SessionId != sessionId)
            return false;
        return ((ICollection<KeyValuePair<string, Session>>)_byUuid)
            .Remove(new KeyValuePair<string, Session>(uuid, current));
    }

    public Session? Get(string uuid) => _byUuid.GetValueOrDefault(uuid);

    // 書き込み検証: uuid の現在 session が server と sessionId の両方に一致するか(VaultProvider 8.2)。
    public bool Validate(string uuid, string server, string sessionId)
    {
        var s = Get(uuid);
        return s != null
               && string.Equals(s.Server, server, StringComparison.Ordinal)
               && string.Equals(s.SessionId, sessionId, StringComparison.Ordinal);
    }

    // WS 切断時: その接続で claim された全 session を失効させ、失効分を返す。
    public List<Session> ExpireConnection(string connectionId)
    {
        var expired = new List<Session>();
        foreach (var pair in _byUuid)
        {
            if (pair.Value.ConnectionId == connectionId &&
                ((ICollection<KeyValuePair<string, Session>>)_byUuid).Remove(pair))
            {
                expired.Add(pair.Value);
            }
        }

        return expired;
    }

    public int Count => _byUuid.Count;
}
