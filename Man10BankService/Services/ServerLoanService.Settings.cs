namespace Man10BankService.Services;

// サーバーローンの設定値。appsettings.json の ServerLoan セクションから読み込む。
// static に持っているので、利息や返済額を計算する static メソッドからそのまま参照できる。
// ServerLoanService のインスタンスを作るたびに Configure で読み直す。
public partial class ServerLoanService
{
    // ===== 設定値 =====

    // 日利(複利)。毎日「残債 × 日利」を残債に加える。
    private static decimal DailyInterestRate { get; set; } = 0.02m;
    // 借入枠の下限(初期枠)
    private static decimal MinAmount { get; set; } = 100000m;
    // 借入枠の上限
    private static decimal MaxAmount { get; set; } = 3000000m;
    // 利息で増える残債(元本 + 利息)の上限。0 以下なら上限なし。
    private static decimal MaxDebtAmount { get; set; } = 10000000m;
    // 未払いの利息(残債 − 元本)の上限を、元本の何倍までにするか。0 以下なら上限なし。
    // 例: 1 なら、利息は元本と同額まで(残債は元本の 2 倍まで)しか増えない。
    private static decimal MaxInterestMultiplier { get; set; }
    // 定期返済の成功時に、「返済額」と「その週の平均残債」の小さいほうへ掛けて枠に加える係数
    private static decimal GrowthFactor { get; set; } = 0.5m;
    // 1回の増枠の上限(その時点の枠に対する割合)
    private static decimal MaxGrowthRate { get; set; } = 0.1m;
    // 定期返済の失敗時に枠へ掛ける率
    private static decimal FailureCutRate { get; set; } = 0.5m;
    // 最低返済額の割合(残債に対して)
    private static decimal MinPaymentRate { get; set; } = 0.02m;
    // 最低返済額の固定下限
    private static decimal MinPaymentAmount { get; set; } = 10000m;
    // 最低返済額で、1週間分の利息に上乗せして返す元本の割合(残債に対して)。0 以下なら利息を見ない。
    private static decimal MinPrincipalRate { get; set; } = 0.01m;
    // 参加者がいる契約(連携契約)の借入枠の上限。未設定なら MaxAmount と同じ。
    private static decimal? LinkedMaxAmount { get; set; }
    // 参加者がいる契約(連携契約)の日利。未設定なら DailyInterestRate と同じ。
    private static decimal? LinkedDailyInterestRate { get; set; }
    // 日次処理(利息の加算・延滞分の回収)の時刻(UTC)
    private static TimeSpan DailyInterestTime { get; set; } = new(2, 0, 0); // 02:00
    // 定期返済の時刻(UTC)と曜日
    private static TimeSpan WeeklyRepayTime { get; set; } = new(3, 0, 0);   // 03:00
    private static DayOfWeek WeeklyRepayDay { get; set; } = DayOfWeek.Monday;

    // ===== 固定値 =====

    // 定期返済の間隔(日数)。増枠の平均残債と、最低返済額に含める利息の算定に使う。
    private const int RepayIntervalDays = 7;
    // 支払額(設定)の初期値を、利息の何週間分にするか。
    private const int InitialPaymentWeeks = 2;

    // スケジューラ(BackgroundService)が参照する実行時刻設定
    public static TimeSpan DailyInterestTimeOfDay => DailyInterestTime;
    public static TimeSpan WeeklyRepayTimeOfDay => WeeklyRepayTime;
    public static DayOfWeek WeeklyRepayDayOfWeek => WeeklyRepayDay;

    // appsettings.json の ServerLoan セクションを読み込む。書かれていない項目は既定値になる。
    private static void Configure(IConfiguration configuration)
    {
        var s = configuration.GetSection("ServerLoan");
        DailyInterestRate = s.GetValue("DailyInterestRate", 0.02m);
        MinAmount = s.GetValue("MinAmount", 100000m);
        MaxAmount = s.GetValue("MaxAmount", 3000000m);
        MaxDebtAmount = s.GetValue("MaxDebtAmount", 10000000m);
        MaxInterestMultiplier = s.GetValue("MaxInterestMultiplier", 0m);
        GrowthFactor = s.GetValue("GrowthFactor", 0.5m);
        MaxGrowthRate = s.GetValue("MaxGrowthRate", 0.1m);
        FailureCutRate = s.GetValue("FailureCutRate", 0.5m);
        MinPaymentRate = s.GetValue("MinPaymentRate", 0.02m);
        MinPaymentAmount = s.GetValue("MinPaymentAmount", 10000m);
        MinPrincipalRate = s.GetValue("MinPrincipalRate", 0.01m);
        LinkedMaxAmount = s.GetValue<decimal?>("LinkedMaxAmount");
        LinkedDailyInterestRate = s.GetValue<decimal?>("LinkedDailyInterestRate");

        if (TimeSpan.TryParse(s["DailyInterestTime"], out var dit))
            DailyInterestTime = dit;
        if (TimeSpan.TryParse(s["WeeklyRepayTime"], out var wrt))
            WeeklyRepayTime = wrt;
        if (Enum.TryParse<DayOfWeek>(s["WeeklyRepayDay"], true, out var wrd))
            WeeklyRepayDay = wrd;
    }
}
