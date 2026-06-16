namespace Man10BankService.Services;

// 残高変更の「確定(コミット)後」に、対象 UUID が在席する 1 サーバーへ
// 確定残高+version を targeting push するための通知抽象(VaultProvider 5.3 / 7.3)。
// 実体は VaultWsHub。テストではフェイクへ差し替える。
public interface IVaultNotifier
{
    // 対象 UUID の在席接続へ push する。在席不明なら何もしない。
    // コミット後に呼ぶこと(コミット前に流すと未確定値が漏れる)。
    Task PushBalanceAsync(VaultBalanceChange change);
}

// push 1件の内容。確定後の残高と単調増加 version、変更原因を運ぶ(VaultProvider 5.3)。
// Cause: DEPOSIT | WITHDRAW | TRANSFER | SET | BANK_MOVE
public sealed record VaultBalanceChange(
    string Uuid,
    decimal Balance,
    long Version,
    string Cause,
    string OriginServer);
