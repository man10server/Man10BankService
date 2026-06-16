using Xunit;

namespace Test.Infrastructure;

// MySQL(Testcontainers)を使うテストは単一の静的コンテナを共有し、
// 各テストが ResetDatabase でスキーマを再作成する。並列実行すると相互にスキーマを
// 破壊し合うため、同一コレクションへ束ねてコレクション間の並列を無効化する。
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MySqlCollection
{
    public const string Name = "MySQL Testcontainers";
}
