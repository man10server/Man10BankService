namespace Man10BankService.Services;

// session / presence チャネル(WebSocket)への抽象。実体は VaultWsHub。テストではフェイクへ差し替える。
// 残高 push は行わない。残高の収束は claim 時ロード+確定応答+定期再同期で行う(VaultProvider 7.4)。
public interface IVaultSessionChannel
{
    // server の同期チャネル接続があれば接続IDを返す(claim の前提条件。無ければ null)。
    // session の寿命をこの接続に紐づけることで、切断したサーバーの claim を確実に失効できる。
    string? GetConnectionId(string server);

    // 後勝ち claim で失効した旧 session を旧サーバーへ通知する(ベストエフォート。VaultProvider 7.4)。
    Task NotifySessionRevokedAsync(string server, string uuid, string sessionId, string newServer);
}
