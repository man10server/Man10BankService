namespace Man10BankService.Models.Responses;

public sealed record HealthPayload(
    string Service,
    DateTime ServerTimeUtc,
    DateTime StartedAtUtc,
    long UptimeSeconds,
    bool Database
);

