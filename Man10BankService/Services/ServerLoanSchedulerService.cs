namespace Man10BankService.Services;

// サーバーローンの日次利息・週次返済を実行するスケジューラ。
// ※ ロジックは ServerLoanService から移動したのみ。冪等化(DB由来判定)はS3で対応する。
public sealed class ServerLoanSchedulerService(ServerLoanService loanService, ILogger<ServerLoanSchedulerService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DateOnly? lastDailyRun = null;
        DateOnly? lastWeeklyRun = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;
                var today = DateOnly.FromDateTime(now);

                var dailyDue = now.TimeOfDay >= ServerLoanService.DailyInterestTimeOfDay;
                if (dailyDue && lastDailyRun != today)
                {
                    await loanService.RunDailyInterestForAllAsync();
                    lastDailyRun = today;
                }

                var weeklyDue = now.DayOfWeek == ServerLoanService.WeeklyRepayDayOfWeek
                                && now.TimeOfDay >= ServerLoanService.WeeklyRepayTimeOfDay;
                if (weeklyDue && lastWeeklyRun != today)
                {
                    await loanService.RunWeeklyRepayForAllAsync();
                    lastWeeklyRun = today;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "サーバーローンのスケジューラ実行中にエラーが発生しました。");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}
