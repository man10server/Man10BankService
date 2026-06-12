using System.Text.RegularExpressions;

namespace Man10BankService.Validation;

// UUID(8-4-4-4-12 形式)の厳密検証を一元化する。
public static partial class UuidValidation
{
    // ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$
    public const string Pattern =
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";

    [GeneratedRegex(Pattern)]
    private static partial Regex UuidRegex();

    public static bool IsValid(string? value) =>
        !string.IsNullOrEmpty(value) && UuidRegex().IsMatch(value);
}
