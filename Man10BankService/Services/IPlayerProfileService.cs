namespace Man10BankService.Services;

public interface IPlayerProfileService
{
    Task<string?> GetNameByUuidAsync(string uuid, CancellationToken ct = default);
}
