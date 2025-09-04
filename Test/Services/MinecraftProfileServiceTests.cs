using FluentAssertions;
using Man10BankService.Services;

namespace Test.Services;

public class MinecraftProfileServiceTests
{
    [Fact(DisplayName = "UUIDが空や不正形式ならnullを返す")]
    public async Task GetNameByUuid_ShouldReturnNull_WhenInvalid()
    {
        (await MinecraftProfileService.GetNameByUuidAsync("")).Should().BeNull();
        (await MinecraftProfileService.GetNameByUuidAsync("not-a-uuid")).Should().BeNull();
        (await MinecraftProfileService.GetNameByUuidAsync("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")).Should().BeNull();
    }
}
