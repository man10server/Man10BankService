using FluentAssertions;
using Man10BankService.Services;

namespace Test.Services;

public class MinecraftProfileServiceTests
{
    private const string KnownUuid = "9c4161a9-0f5f-4317-835c-0bb196a7defa"; // テスト用に自由に変更可
    private const string KnownName = "forest611"; // テスト用に自由に変更可

    [Fact(DisplayName = "UUIDが存在する場合にMCIDを返す")]
    public async Task GetNameByUuid_ShouldReturnName_WhenExists()
    {
        var res = await MinecraftProfileService.GetNameByUuidAsync(KnownUuid);

        res.StatusCode.Should().Be(200);
        res.Data.Should().Be(KnownName);
    }

    [Fact(DisplayName = "UUIDが存在しない場合は404を返す")]
    public async Task GetNameByUuid_Should404_WhenNotFound()
    {
        var res = await MinecraftProfileService.GetNameByUuidAsync("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        res.StatusCode.Should().Be(404);
    }
}
