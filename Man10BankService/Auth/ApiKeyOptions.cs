namespace Man10BankService.Auth;

// appsettings の Auth セクションをバインドするための設定モデル
public sealed class ApiKeyAuthSettings
{
    public List<ApiKeyEntry> ApiKeys { get; set; } = [];
}

// 個々のAPIキー定義（キー本体・表示名・スコープ）
public sealed class ApiKeyEntry
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = [];
}
