namespace Man10BankService.Services;

// appsettings.json "Vault" セクションの設定。GET /api/Vault/config で Paper へ配布する。
public sealed class VaultOptions
{
    // 電子マネー残高上限。既定 1 兆円。残高を増やす操作・絶対値設定の更新後残高を検証する。
    public decimal MaxBalance { get; set; } = 1_000_000_000_000m;

    // join 後、Provider キャッシュを WARMING_UP に置く時間(ms)。
    public long JoinReadyDelayMillis { get; set; } = 3000;

    // quit / transfer 時に送信待ちキュー flush を待つ最大時間(ms)。
    public long QuitDrainTimeoutMillis { get; set; } = 3000;

    // IEEE 754 double で整数を正確に表現できる上限(9_007_199_254_740_991)。
    // 設計書 §12: 上限値はこれ以下に限定する。
    public const decimal SafeMaxBalanceCeiling = 9_007_199_254_740_991m;

    // MaxBalance が有効か(1 以上かつ安全上限以下の整数)。不正なら Vault 書き込みを fail-closed にする。
    public bool IsMaxBalanceValid =>
        MaxBalance >= 1m
        && MaxBalance <= SafeMaxBalanceCeiling
        && MaxBalance == decimal.Truncate(MaxBalance);
}
