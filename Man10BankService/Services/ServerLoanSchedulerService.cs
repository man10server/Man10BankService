namespace Man10BankService.Services;

// サーバーローンの日次処理(利息の加算・延滞分の回収)と週次の定期返済(旧リボの分を含む)を、決まった時刻に実行する。
// 1 分ごとに現在時刻(UTC)を見て、実行時刻を過ぎていて今日まだ実行していなければ実行する。
// Scheduler:Enabled が true のインスタンスでだけ動く(Program.cs)。
public sealed class ServerLoanSchedulerService(
    ServerLoanService loanService, LegacyServerLoanService legacyLoanService, ILogger<ServerLoanSchedulerService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 今日の日次処理を済ませたかどうか。最後まで走り切ったときだけ更新する。
        // メモリにしか持たないので再起動すると忘れるが、もう一度走っても二重にはならない:
        // 利息は契約ごとに当日分のログがあれば付けず、延滞分の回収は未回収の額までしか行わない。
        DateOnly? lastDailyRun = null;
        // 今日の定期返済を済ませたかどうか。こちらもメモリにしか持たず、DB での判定は無い。
        // 返済日に実行した後、同じ日のうちに再起動すると、もう一度引き落とされる(既知の制約)。
        DateOnly? lastWeeklyRun = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var today = DateOnly.FromDateTime(now);

                var dailyDue = now.TimeOfDay >= ServerLoanService.DailyInterestTimeOfDay;
                if (dailyDue && lastDailyRun != today)
                {
                    await loanService.RunDailyInterestForAllAsync(today);
                    await loanService.RunOverdueCollectionForAllAsync();
                    lastDailyRun = today;
                }

                var weeklyDue = now.DayOfWeek == ServerLoanService.WeeklyRepayDayOfWeek
                                && now.TimeOfDay >= ServerLoanService.WeeklyRepayTimeOfDay;
                if (weeklyDue && lastWeeklyRun != today)
                {
                    // 旧リボの定期返済も同じ時刻に行う(旧リボの廃止時に削除する)。
                    await legacyLoanService.RunWeeklyRepayForAllAsync();
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
