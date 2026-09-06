namespace Man10BankService.Validation;

// Vault(電子マネー)リクエスト金額の静的な絶対上限。
// IEEE 754 Double で整数を正確に表現できる最大値(2^53 - 1)。
// 運用上の上限は Vault:MaxBalance(既定1兆)が権威で、VaultService が検証する(VaultProvider 12)。
public static class VaultAmountLimits
{
    public const decimal MaxSafe = 9_007_199_254_740_991m;

    // Range 属性に渡す文字列(typeof(decimal) と併用)。
    public const string MaxSafeText = "9007199254740991";
    public const string MinText = "0";
}
