using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Man10BankService.Services;

namespace Man10BankService.Hubs;

// 電子マネー残高確定 push を WebSocket で全接続へブロードキャストするハブ。
// 各 Paper の VaultService は自分がキャッシュしている UUID だけを反映するため、
// 在席管理を持たずに全接続へ配信して受信側でフィルタする簡素モデルを採る。
//
// 接続ごとに送信専用の Outbox チャネルを持ち、単一の書き込みループで送る。
// WebSocket.SendAsync はスレッドセーフでないため、送信は必ずこのループだけが行う。
public sealed class VaultWsHub : IVaultNotifier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class Connection
    {
        public required WebSocket Socket { get; init; }
        public required Channel<string> Outbox { get; init; }
    }

    private readonly ConcurrentDictionary<Guid, Connection> _connections = new();
    private readonly ILogger<VaultWsHub> _logger;

    public VaultWsHub(ILogger<VaultWsHub> logger)
    {
        _logger = logger;
    }

    public int ConnectionCount => _connections.Count;

    // 残高確定 push。全接続の Outbox へ JSON を投入する(送信はループが行う)。
    public Task PushBalanceAsync(VaultBalanceChange change, CancellationToken ct = default)
    {
        if (_connections.IsEmpty) return Task.CompletedTask;

        var payload = JsonSerializer.Serialize(new
        {
            type = "vault.balance",
            uuid = change.Uuid,
            balance = change.Balance,
            version = change.Version,
            cause = change.Cause,
            operationId = change.OperationId,
            originServer = change.OriginServer
        }, JsonOptions);

        foreach (var conn in _connections.Values)
        {
            // 受信が詰まっている接続は取りこぼすが、Paper 側は定期再同期で収束する。
            conn.Outbox.Writer.TryWrite(payload);
        }

        return Task.CompletedTask;
    }

    // 受理済み WebSocket を接続が閉じるまで処理する。送信ループと受信(クローズ検知)ループを並走させる。
    public async Task HandleConnectionAsync(WebSocket socket, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var conn = new Connection
        {
            Socket = socket,
            Outbox = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            })
        };
        _connections[id] = conn;
        _logger.LogInformation("Vault WebSocket 接続を受理しました(connections={Count})。", _connections.Count);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            var writeTask = WriteLoopAsync(conn, cts.Token);
            var readTask = ReadLoopAsync(conn, cts.Token);
            await Task.WhenAny(writeTask, readTask);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Vault WebSocket 接続処理で例外が発生しました。");
        }
        finally
        {
            await cts.CancelAsync();
            _connections.TryRemove(id, out _);
            _logger.LogInformation("Vault WebSocket 接続を切断しました(connections={Count})。", _connections.Count);
        }
    }

    private static async Task WriteLoopAsync(Connection conn, CancellationToken ct)
    {
        await foreach (var msg in conn.Outbox.Reader.ReadAllAsync(ct))
        {
            if (conn.Socket.State != WebSocketState.Open) break;
            var bytes = Encoding.UTF8.GetBytes(msg);
            await conn.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
    }

    // 受信は使わないが、クローズフレーム検知のために読み続ける。
    private static async Task ReadLoopAsync(Connection conn, CancellationToken ct)
    {
        var buffer = new byte[1024];
        while (conn.Socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await conn.Socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await conn.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None);
                break;
            }
        }
    }
}
