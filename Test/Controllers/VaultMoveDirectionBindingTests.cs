using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Man10BankService.Models.Requests;

namespace Test.Controllers;

// move の direction は JSON 文字列("VaultToBank"/"BankToVault")で授受する。
// Program.cs は AddJsonOptions で JsonStringEnumConverter を登録しており、本テストは
// 同じ設定で Kotlin が送る JSON が正しく enum へバインドされることを担保する。
public class VaultMoveDirectionBindingTests
{
    private static readonly JsonSerializerOptions Options = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        // Program.cs の AddControllers().AddJsonOptions と同等（既定の Web 設定 + 文字列 enum）。
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    [Theory]
    [InlineData("VaultToBank", VaultMoveDirection.VaultToBank)]
    [InlineData("BankToVault", VaultMoveDirection.BankToVault)]
    public void Direction_BindsFromString(string value, VaultMoveDirection expected)
    {
        var json = $$"""
        {"uuid":"9c4161a9-0f5f-4317-835c-0bb196a7defa","amount":100,"direction":"{{value}}",
         "pluginName":"t","note":"n","displayNote":"d","server":"s"}
        """;
        var req = JsonSerializer.Deserialize<VaultMoveRequest>(json, Options);
        req.Should().NotBeNull();
        req!.Direction.Should().Be(expected);
        req.Amount.Should().Be(100m);
    }
}
