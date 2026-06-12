namespace Man10BankService.Validation;

// 金額リクエストの上限(1兆)。Range 属性の文字列表現と数値の両方で参照する。
public static class AmountLimits
{
    public const decimal Max = 1_000_000_000_000m;

    // Range 属性に渡す文字列(typeof(decimal) と併用)。
    public const string MaxText = "1000000000000";
    public const string MinText = "0";
}
