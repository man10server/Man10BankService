using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Man10BankService.Services;

namespace Man10BankService.Hubs;

// 各 Paper サーバーの session / presence WebSocket チャネル(VaultProvider 7.4)。
// - 残高 push は行わない。送るのは ping/pong と session 失効通知だけ。
// - 接続はサーバー名ごとに 1 本(同名の新規接続は旧接続を置き換える)。
// - ping/pong で生存監視し、切断検知時はその接続で claim された session を一括失効する
//   (session の寿命=この接続。切断中のサーバーからの書き込みは SessionMismatch で拒否される)。
// - 詰まった接続(バックプレッシャ)は切断する。
public sealed class VaultWsHub : IVaultSessionChannel
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

    private readonly VaultSessionManager _sessions;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Connection> _byServer =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<VaultWsHub>? _logger;

    public VaultWsHub(VaultSessionManager sessions, ILogger<VaultWsHub>? logger = null)
    {
        _sessions = sessions;
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

    public int ConnectionCount => _byServer.Count;

    // === IVaultSessionChannel ===

    // server の接続があれば接続IDを返す(claim の前提条件。VaultProvider 5.8)。
    public string? GetConnectionId(string server)
    {
        return _byServer.TryGetValue(server, out var conn) && !conn.Cts.IsCancellationRequested
            ? conn.Id
            : null;
    }

    // 後勝ち claim で失効した旧 session を旧サーバーへ通知する(接続が無ければ何もしない)。
    public Task NotifySessionRevokedAsync(string server, string uuid, string sessionId, string newServer)
    {
        if (!_byServer.TryGetValue(server, out var conn))
            return Task.CompletedTask;

        var json = BuildSessionRevokedJson(uuid, sessionId, newServer);
        if (!conn.Outbox.Writer.TryWrite(json))
        {
            // バックプレッシャ: キューが詰まった接続は切断する。session は切断処理で一括失効する。
            _logger?.LogWarning("送信キューが詰まったため接続を切断します server={Server}", conn.ServerName);
            conn.Cts.Cancel();
        }

        return Task.CompletedTask;
    }

    // WebSocket 接続を受け付け、切断まで読み書きループを回す(VaultController.Ws から呼ばれる)。
    // 同名サーバーの既存接続は置き換え、旧接続は閉じる(旧接続の session は旧接続IDに紐づくため、
    // 旧接続のクリーンアップで新接続の session を巻き込まない)。
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

        _byServer.AddOrUpdate(serverName, conn, (_, old) => ReplaceOld(old, conn));
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
            // この接続で claim された session を一括失効する(接続=リースの寿命)。
            var expired = _sessions.ExpireConnection(conn.Id);
            // 自分が現役登録のままなら取り除く(既に新接続で置き換え済みなら何もしない)。
            ((ICollection<KeyValuePair<string, Connection>>)_byServer)
                .Remove(new KeyValuePair<string, Connection>(serverName, conn));
            conn.Outbox.Writer.TryComplete();

            try { await writer; } catch { /* ignore */ }
            try { await pinger; } catch { /* ignore */ }

            await TryCloseAsync(conn);
            _logger?.LogInformation(
                "Vault WebSocket 切断: server={Server} id={Id} session失効={Expired}件",
                serverName, conn.Id, expired.Count);
        }
    }

    private Connection ReplaceOld(Connection old, Connection replacement)
    {
        _logger?.LogInformation("同名サーバーの旧 WebSocket 接続を置き換えます server={Server}", old.ServerName);
        old.Cts.Cancel();
        return replacement;
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
            case "pong":
                conn.LastPongTicks = Environment.TickCount64;
                break;
            case "ping":
                // クライアント起点の ping には pong を返す。
                conn.Outbox.Writer.TryWrite("{\"type\":\"pong\"}");
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

    // session 失効通知(VaultProvider 7.4)の JSON を組み立てる。
    public static string BuildSessionRevokedJson(string uuid, string sessionId, string newServer)
    {
        var evt = new SessionRevokedEvent("session_revoked", uuid, sessionId, newServer);
        return JsonSerializer.Serialize(evt, JsonOptions);
    }

    private sealed record SessionRevokedEvent(
        string Type,
        string Uuid,
        string SessionId,
        string NewServer);

    private sealed record InboundMessage(
        string? Type,
        string? Uuid,
        string? Server);
}
