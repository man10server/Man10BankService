namespace Man10BankService.Services;

// サーバー全体の資産スナップショットを毎時記録するスケジューラ。
// 冪等化(DESIGN 2.4): 実行済み判定は DB 由来(同一時刻のスナップショット存在)で行い、
// インメモリの lastRun 変数を廃止。再起動後も同一時刻の重複INSERTをしない。
public sealed class ServerEstateSchedulerService(ServerEstateService estateService, ILogger<ServerEstateSchedulerService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nowUtc = DateTime.UtcNow;
                var currentHourUtc = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, DateTimeKind.Utc);
                if (!await estateService.HasSnapshotForHourAsync(currentHourUtc))
                {
                    await estateService.RecordSnapshotAsync(currentHourUtc);
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
