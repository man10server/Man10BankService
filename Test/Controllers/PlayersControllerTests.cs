using FluentAssertions;
using Man10BankService.Controllers;
using Man10BankService.Models.Requests;
using Man10BankService.Models.Responses;
using Man10BankService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Extensions.DependencyInjection;
using Test.Infrastructure;

namespace Test.Controllers;

// プレイヤー名簿 API(POST /api/Players, GET /api/Players/{uuid}, GET /api/Players/by-name/{name})の検証。
public class PlayersControllerTests
{
    private const string Uuid = "9c4161a9-0f5f-4317-835c-0bb196a7defa";
    private const string OtherUuid = "49c42256-2357-4963-8678-7a06e6dd3125";

    private static ControllerHost BuildController()
    {
        var db = TestDbFactory.Create();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(PlayersController).Assembly);
        var sp = services.BuildServiceProvider();

        var ctrl = new PlayersController(new PlayerRegistryService(db.Factory))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ObjectValidator = sp.GetRequiredService<IObjectModelValidator>(),
            MetadataProvider = sp.GetRequiredService<IModelMetadataProvider>()
        };
        return new ControllerHost { Controller = ctrl, Resources = [db, sp] };
    }

    private static PlayerProfileResponse Ok(ActionResult<PlayerProfileResponse> res) =>
        res.Result.Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<PlayerProfileResponse>().Which;

    private static void NotFound(ActionResult<PlayerProfileResponse> res) =>
        res.Result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<ProblemDetails>()
            .Which.Extensions["code"].Should().Be(ErrorCode.PlayerNotFound.ToString());

    [Fact(DisplayName = "名簿: 登録するとUUIDと名前の両方から引ける")]
    public async Task Register_ThenLookup_ByUuidAndName()
    {
        using var host = BuildController();
        var ctrl = (PlayersController)host.Controller;

        var registered = Ok(await ctrl.Register(new PlayerRegisterRequest { Uuid = Uuid, Name = "Steve" }));
        registered.Uuid.Should().Be(Uuid);
        registered.Name.Should().Be("Steve");

        Ok(await ctrl.GetByUuid(Uuid)).Name.Should().Be("Steve");
        Ok(await ctrl.GetByName("Steve")).Uuid.Should().Be(Uuid);
        // 名前の照合は大文字小文字を区別しない
        Ok(await ctrl.GetByName("steve")).Uuid.Should().Be(Uuid);
    }

    [Fact(DisplayName = "名簿: 同じUUIDを別名で再登録すると名前が更新され、旧名では引けなくなる(改名の追従)")]
    public async Task Register_SameUuidNewName_ShouldUpdateName()
    {
        using var host = BuildController();
        var ctrl = (PlayersController)host.Controller;

        Ok(await ctrl.Register(new PlayerRegisterRequest { Uuid = Uuid, Name = "OldName" }));
        Ok(await ctrl.Register(new PlayerRegisterRequest { Uuid = Uuid, Name = "NewName" }));

        Ok(await ctrl.GetByUuid(Uuid)).Name.Should().Be("NewName");
        Ok(await ctrl.GetByName("NewName")).Uuid.Should().Be(Uuid);
        NotFound(await ctrl.GetByName("OldName"));
    }

    [Fact(DisplayName = "名簿: 同じ名前を後から別UUIDが名乗った場合は最後にログインした人を返す")]
    public async Task GetByName_ReusedName_ShouldReturnMostRecent()
    {
        using var host = BuildController();
        var ctrl = (PlayersController)host.Controller;

        Ok(await ctrl.Register(new PlayerRegisterRequest { Uuid = Uuid, Name = "Shared" }));
        await Task.Delay(20);
        Ok(await ctrl.Register(new PlayerRegisterRequest { Uuid = OtherUuid, Name = "Shared" }));

        Ok(await ctrl.GetByName("Shared")).Uuid.Should().Be(OtherUuid);
    }

    [Fact(DisplayName = "名簿: 未登録のUUID/名前は404(PlayerNotFound)")]
    public async Task Lookup_Unknown_ShouldReturn404()
    {
        using var host = BuildController();
        var ctrl = (PlayersController)host.Controller;

        NotFound(await ctrl.GetByUuid(Uuid));
        NotFound(await ctrl.GetByName("nobody"));
    }

    [Fact(DisplayName = "名簿: UUIDは小文字に正規化して保存され、大文字で問い合わせても引ける")]
    public async Task Register_UppercaseUuid_ShouldBeNormalized()
    {
        using var host = BuildController();
        var ctrl = (PlayersController)host.Controller;

        var registered = Ok(await ctrl.Register(new PlayerRegisterRequest { Uuid = Uuid.ToUpperInvariant(), Name = "Alex" }));
        registered.Uuid.Should().Be(Uuid);
        Ok(await ctrl.GetByUuid(Uuid.ToUpperInvariant())).Name.Should().Be("Alex");
    }

    [Fact(DisplayName = "名簿: 不正なUUID/空の名前/17文字以上の名前はモデル検証で弾かれる")]
    public void Register_Invalid_ShouldFailModelValidation()
    {
        using var host = BuildController();
        var ctrl = (PlayersController)host.Controller;

        ctrl.TryValidateModel(new PlayerRegisterRequest { Uuid = "not-a-uuid", Name = "Steve" }).Should().BeFalse();
        ctrl.ModelState.Clear();
        ctrl.TryValidateModel(new PlayerRegisterRequest { Uuid = Uuid, Name = "" }).Should().BeFalse();
        ctrl.ModelState.Clear();
        ctrl.TryValidateModel(new PlayerRegisterRequest { Uuid = Uuid, Name = new string('a', 17) }).Should().BeFalse();
        ctrl.ModelState.Clear();
        ctrl.TryValidateModel(new PlayerRegisterRequest { Uuid = Uuid, Name = "Steve" }).Should().BeTrue();
    }
}
