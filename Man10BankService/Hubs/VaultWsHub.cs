using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Man10BankService.Services;

namespace Man10BankService.Hubs;

// 各 Paper サーバーの WebSocket 接続と presence を管理するシングルトン(VaultProvider 7.3)。
// - 残高変更は「コミット後」に対象 UUID の在席接続へ targeting push する(在席不明なら送らない)。
// - ping/pong で生存監視し、切断検知時はその接続の presence を一括失効する。
// - 詰まった接続(バックプレッシャ)は切断する。
public sealed class VaultWsHub : IVaultNotifier
{
    private const int OutboxCapacity = 256;
    private const int ReceiveBufferSize = 8 * 1024;
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(20);
    private static readonly long PongTimeoutMs = (long)TimeSpan.FromSeconds(60).TotalMilliseconds;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly VaultPresenceRegistry _presence = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Connection> _connections = new();
    private readonly ILogger<VaultWsHub>? _logger;

    public VaultWsHub(ILogger<VaultWsHub>? logger = null)
    {
        _logger = logger;
    }

    // 1 Paper サーバーぶんの接続。送信は Outbox を介して WriteLoop が唯一の送信者になる
    // (WebSocket.SendAsync は同時呼び出し不可のため)。
    private sealed class Connection
    {
        public required string Id { get; init; }
        public required string ServerName { get; init; }
        public required WebSocket Socket { get; init; }
        public required Channel<string> Outbox { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public long LastPongTicks;
    }

    public int ConnectionCount => _connections.Count;
    public int PresenceCount => _presence.Count;

    // === IVaultNotifier ===
    // 対象 UUID の在席接続へ残高イベントを enqueue する(在席不明なら無視)。
    // 同期的にキュー投入するだけで送信完了は待たない(ホットパスをブロックしない)。
    public Task PushBalanceAsync(VaultBalanceChange change)
    {
        if (_presence.Find(change.Uuid) is not Connection conn)
            return Task.CompletedTask;

        var json = BuildBalanceEventJson(change);
        if (!conn.Outbox.Writer.TryWrite(json))
        {
            // バックプレッシャ: キューが詰まった接続は切断する。再接続時に full resync で回復する。
            _logger?.LogWarning("送信キューが詰まったため接続を切断します server={Server}", conn.ServerName);
            conn.Cts.Cancel();
        }

        return Task.CompletedTask;
    }

    // WebSocket 接続を受け付け、切断まで読み書きループを回す(VaultController.Ws から呼ばれる)。
    public async Task HandleConnectionAsync(WebSocket socket, string serverName, CancellationToken requestAborted)
    {
        var conn = new Connection
        {
            Id = Guid.NewGuid().ToString("N"),
            ServerName = serverName,
            Socket = socket,
            Outbox = Channel.CreateBounded<string>(new BoundedChannelOptions(OutboxCapacity)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait
            }),
            Cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted),
            LastPongTicks = Environment.TickCount64
        };

        _connections[conn.Id] = conn;
        _logger?.LogInformation("Vault WebSocket 接続: server={Server} id={Id}", serverName, conn.Id);

        var writer = Task.Run(() => WriteLoopAsync(conn));
        var pinger = Task.Run(() => PingLoopAsync(conn));

        try
        {
            await ReadLoopAsync(conn);
        }
        catch (Exception e)
        {
            _logger?.LogDebug(e, "Vault WebSocket 読み取りループが終了しました server={Server}", serverName);
        }
        finally
        {
            conn.Cts.Cancel();
            var expired = _presence.ExpireConnection(conn);
            _connections.TryRemove(conn.Id, out _);
            conn.Outbox.Writer.TryComplete();

            try { await writer; } catch { /* ignore */ }
            try { await pinger; } catch { /* ignore */ }

            await TryCloseAsync(conn);
            _logger?.LogInformation(
                "Vault WebSocket 切断: server={Server} id={Id} presence失効={Expired}件",
                serverName, conn.Id, expired);
        }
    }

    private async Task ReadLoopAsync(Connection conn)
    {
        var buffer = new byte[ReceiveBufferSize];
        while (!conn.Cts.IsCancellationRequested)
        {
            var text = await ReceiveTextAsync(conn, buffer);
            if (text == null)
                break; // Close フレーム or 接続断

            if (string.IsNullOrWhiteSpace(text))
                continue;

            Dispatch(conn, text);
        }
    }

    private async Task<string?> ReceiveTextAsync(Connection conn, byte[] buffer)
    {
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await conn.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), conn.Cts.Token);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private void Dispatch(Connection conn, string text)
    {
        InboundMessage? msg;
        try
        {
            msg = JsonSerializer.Deserialize<InboundMessage>(text, JsonOptions);
        }
        catch (JsonException)
        {
            _logger?.LogDebug("不正な WebSocket メッセージを無視: server={Server}", conn.ServerName);
            return;
        }

        if (msg?.Type == null)
            return;

        switch (msg.Type.ToLowerInvariant())
        {
            case "presence":
                HandlePresence(conn, msg);
                break;
            case "pong":
                conn.LastPongTicks = Environment.TickCount64;
                break;
            case "ping":
                // クライアント起点の ping には pong を返す。
                conn.Outbox.Writer.TryWrite("{\"type\":\"pong\"}");
                break;
        }
    }

    private void HandlePresence(Connection conn, InboundMessage msg)
    {
        if (string.IsNullOrWhiteSpace(msg.Uuid) || string.IsNullOrWhiteSpace(msg.Action))
            return;

        switch (msg.Action.ToLowerInvariant())
        {
            case "join":
                // サーバー移動で別接続から join された場合は後勝ちで上書き。
                _presence.Join(msg.Uuid, conn);
                break;
            case "quit":
                _presence.Quit(msg.Uuid, conn);
                break;
        }
    }

    private async Task WriteLoopAsync(Connection conn)
    {
        try
        {
            await foreach (var json in conn.Outbox.Reader.ReadAllAsync(conn.Cts.Token))
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                await conn.Socket.SendAsync(
                    new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, conn.Cts.Token);
            }
        }
        catch (OperationCanceledException) { /* 正常終了 */ }
        catch (Exception)
        {
            // 送信失敗は接続断とみなし、読み取りループを終了させる。
            conn.Cts.Cancel();
        }
    }

    private async Task PingLoopAsync(Connection conn)
    {
        try
        {
            while (!conn.Cts.IsCancellationRequested)
            {
                await Task.Delay(PingInterval, conn.Cts.Token);

                // pong が一定時間来なければ半開放接続とみなして切断する。
                if (Environment.TickCount64 - conn.LastPongTicks > PongTimeoutMs)
                {
                    _logger?.LogInformation("pong タイムアウトのため接続を切断します server={Server}", conn.ServerName);
                    conn.Cts.Cancel();
                    return;
                }

                conn.Outbox.Writer.TryWrite("{\"type\":\"ping\"}");
            }
        }
        catch (OperationCanceledException) { /* 正常終了 */ }
    }

    private async Task TryCloseAsync(Connection conn)
    {
        try
        {
            if (conn.Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await conn.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cts.Token);
            }
        }
        catch
        {
            // クローズ失敗は無視(既に切断済み)。
        }
    }

    // 残高イベント(VaultProvider 5.3)の JSON を組み立てる。ts は診断用。
    public static string BuildBalanceEventJson(VaultBalanceChange change)
        => BuildBalanceEventJson(change, DateTime.UtcNow);

    public static string BuildBalanceEventJson(VaultBalanceChange change, DateTime ts)
    {
        var evt = new BalanceEvent(
            "balance",
            change.Uuid,
            change.Balance,
            change.Version,
            change.Cause,
            change.OriginServer,
            ts.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
        return JsonSerializer.Serialize(evt, JsonOptions);
    }

    private sealed record BalanceEvent(
        string Type,
        string Uuid,
        decimal Balance,
        long Version,
        string Cause,
        string OriginServer,
        string Ts);

    private sealed record InboundMessage(
        string? Type,
        string? Action,
        string? Uuid,
        string? Server);
}
