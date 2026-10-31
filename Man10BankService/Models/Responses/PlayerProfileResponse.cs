using Man10BankService.Models.Database;

namespace Man10BankService.Models.Responses;

// プレイヤー名簿のレスポンスDTO。JSONは { "uuid": "...", "name": "..." }。
public sealed record PlayerProfileResponse(string Uuid, string Name)
{
    public static PlayerProfileResponse From(PlayerProfile e) => new(e.Uuid, e.Name);
}
