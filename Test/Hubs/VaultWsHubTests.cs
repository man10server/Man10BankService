using FluentAssertions;
using Man10BankService.Hubs;
using Man10BankService.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Test.Hubs;

public class VaultWsHubTests
{
    [Fact(DisplayName = "接続が無い状態で push しても例外にならない")]
    public async Task Push_NoConnections_NoThrow()
    {
        var hub = new VaultWsHub(NullLogger<VaultWsHub>.Instance);
        hub.ConnectionCount.Should().Be(0);

        var change = new VaultBalanceChange("9c4161a9-0f5f-4317-835c-0bb196a7defa", 1234, 5, "DEPOSIT", "op-1", "dev");
        var act = async () => await hub.PushBalanceAsync(change);
        await act.Should().NotThrowAsync();
    }
}
