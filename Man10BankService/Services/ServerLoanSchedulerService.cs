namespace Man10BankService.Services;

// サーバーローンの日次利息・週次返済を実行するスケジューラ。
// 冪等化(DESIGN 2.4): 実行済み判定は DB 由来(当日 Interest ログの存在)で行い、
// インメモリの lastRun 変数を廃止。プロセス再起動後も同日二重課金しない。
public sealed class ServerLoanSchedulerService(ServerLoanService loanService, ILogger<ServerLoanSchedulerService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 日次利息の冪等性はローン単位の DB 判定(HasInterestLogOnDateAsync)が担保する。
        // lastDailyRun は毎分の全件クエリを避けるための省力化マーカーであり、
        // 全件パスが例外なく完走した場合のみ更新する(途中クラッシュ時は次ティックで再走し、
        // 処理済みローンはローン単位の判定でスキップされ、未処理ローンだけが拾われる)。
        DateOnly? lastDailyRun = null;
        // 週次返済はサーバーローンログに専用の冪等マーカーが無いため、当日内の重複実行のみ
        // インメモリで抑止する(再起動時の冪等性は将来課題。日次利息は DB 由来で完全冪等)。
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
