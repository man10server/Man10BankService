using System.Collections.Concurrent;

namespace Man10BankService.Hubs;

// presence(UUID → 接続)を管理する。VaultWsHub から接続ハンドルを object として受け取り、
// ソケット I/O から切り離して単体テスト可能にする(VaultProvider 5.4)。
// オンラインは常に最大1サーバーなので UUID→接続 は 1:1。サーバー移動時は後勝ち(last-write-wins)。
public sealed class VaultPresenceRegistry
{
    private readonly ConcurrentDictionary<string, object> _byUuid = new(StringComparer.OrdinalIgnoreCase);

    // join: その UUID の push 先をこの接続に設定する(既存があれば後勝ちで上書き)。
    public void Join(string uuid, object connection)
    {
        _byUuid[uuid] = connection;
    }

    // quit: 現在この接続に紐づく場合のみ登録解除する(別接続が後勝ちで上書き済みなら何もしない)。
    public bool Quit(string uuid, object connection)
    {
        return ((ICollection<KeyValuePair<string, object>>)_byUuid)
            .Remove(new KeyValuePair<string, object>(uuid, connection));
    }

    // 対象 UUID の在席接続を返す。在席不明なら null。
    public object? Find(string uuid)
    {
        return _byUuid.TryGetValue(uuid, out var conn) ? conn : null;
    }

    // 接続断時にその接続の presence をサーバー単位で一括失効する。失効件数を返す。
    public int ExpireConnection(object connection)
    {
        var removed = 0;
        foreach (var pair in _byUuid)
        {
            if (ReferenceEquals(pair.Value, connection) &&
                ((ICollection<KeyValuePair<string, object>>)_byUuid).Remove(pair))
            {
                removed++;
            }
        }
        return removed;
    }

    public int Count => _byUuid.Count;
}
