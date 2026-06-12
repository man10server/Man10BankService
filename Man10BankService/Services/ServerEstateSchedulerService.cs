namespace Man10BankService.Services;

// サーバー全体の資産スナップショットを毎時記録するスケジューラ。
// ※ ロジックは ServerEstateService から移動したのみ。冪等化(DB由来判定)はS3で対応する。
public sealed class ServerEstateSchedulerService(ServerEstateService estateService, ILogger<ServerEstateSchedulerService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DateTime? lastRunHourUtc = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nowUtc = DateTime.UtcNow;
                var currentHourUtc = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, DateTimeKind.Utc);
                if (lastRunHourUtc != currentHourUtc)
                {
                    await estateService.RecordSnapshotAsync(currentHourUtc);
                    lastRunHourUtc = currentHourUtc;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "サーバー資産スナップショットのスケジューラ実行中にエラーが発生しました。");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}
