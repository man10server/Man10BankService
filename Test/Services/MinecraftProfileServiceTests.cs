using FluentAssertions;
using Man10BankService.Services;

namespace Test.Services;

public class MinecraftProfileServiceTests
{
    [Fact(DisplayName = "UUIDが空や不正形式ならnullを返す")]
    public async Task GetNameByUuid_ShouldReturnNull_WhenInvalid()
    {
        var service = new MojangPlayerProfileService();
        (await service.GetNameByUuidAsync("")).Should().BeNull();
        (await service.GetNameByUuidAsync("not-a-uuid")).Should().BeNull();
        (await service.GetNameByUuidAsync("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")).Should().BeNull();
    }
}
