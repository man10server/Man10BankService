using Man10BankService.Services;

namespace Test.Infrastructure;

public class FakePlayerProfileService : IPlayerProfileService
{
    private readonly Dictionary<string, string?> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _fallbackName;

    public FakePlayerProfileService(string fallbackName = "test-player")
    {
        _fallbackName = fallbackName;
    }

    public void SetName(string uuid, string? name)
    {
        _names[uuid] = name;
    }

    public Task<string?> GetNameByUuidAsync(string uuid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uuid))
            return Task.FromResult<string?>(null);

        if (_names.TryGetValue(uuid, out var name))
            return Task.FromResult(name);

        return Task.FromResult<string?>(_fallbackName);
    }
}
