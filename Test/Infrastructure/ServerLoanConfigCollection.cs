namespace Test.Infrastructure;

// ServerLoanService の設定値は static に保持されるため、
// 設定を変えるテストが他クラスと並列に走らないよう同一コレクションにまとめる。
[CollectionDefinition(Name)]
public sealed class ServerLoanConfigCollection
{
    public const string Name = "ServerLoanConfig";
}
