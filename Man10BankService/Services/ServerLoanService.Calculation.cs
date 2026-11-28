using Man10BankService.Models.Database;

namespace Man10BankService.Services;

// 利息と返済額の計算。どれも DB を読まず、残債・元本と設定値だけで決まる。
// linked は「参加者がいる契約か」で、true のとき連携契約向けの日利(LinkedDailyInterestRate)を使う。
public partial class ServerLoanService
{
    // 契約の日利。参加者がいる契約(linked)は LinkedDailyInterestRate(未設定なら DailyInterestRate)を使う。
    private static decimal GetDailyInterestRate(bool linked)
    {
        return linked ? LinkedDailyInterestRate ?? DailyInterestRate : DailyInterestRate;
    }

    // 1日分の利息: 残債 × 日利(1 円未満は四捨五入)。principal は残債のうちの元本。
    // 次の 2 つの上限を超える分の利息は付けない。
    //  - 残債(元本 + 利息)が MaxDebtAmount を超えない(MaxDebtAmount が 0 以下なら上限なし)。
    //  - 未払いの利息(残債 − 元本)が 元本 × MaxInterestMultiplier を超えない(MaxInterestMultiplier が 0 以下なら上限なし)。
    public static decimal CalculateDailyInterestAmount(decimal debt, decimal principal, bool linked = false)
    {
        if (debt <= 0m)
            return 0m;

        var interest = Math.Round(debt * GetDailyInterestRate(linked), 0, MidpointRounding.AwayFromZero);
        if (MaxDebtAmount > 0m)
            interest = Math.Min(interest, MaxDebtAmount - debt);
        if (MaxInterestMultiplier > 0m)
        {
            var unpaidInterest = debt - principal;
            var maxUnpaidInterest = Math.Floor(principal * MaxInterestMultiplier);
            interest = Math.Min(interest, maxUnpaidInterest - unpaidInterest);
        }
        return Math.Max(0m, interest);
    }

    // 現在の残債に、定期返済の間隔(1週間)ぶんの利息を日次の計算どおりに付けた場合の利息合計。
    private static decimal CalculateWeeklyInterestAmount(decimal debt, decimal principal, bool linked)
    {
        var debtAfterWeek = debt;
        for (var day = 0; day < RepayIntervalDays; day++)
            debtAfterWeek += CalculateDailyInterestAmount(debtAfterWeek, principal, linked);
        return debtAfterWeek - debt;
    }

    // 支払額(設定)の初期値: 単利で InitialPaymentWeeks 週間分の利息(最低 1 円)。
    // 支払額が未設定の状態で借りたときと、運営が残債を調整したときに設定する。
    private static decimal CalculateInitialPaymentAmount(decimal debt, bool linked)
    {
        if (debt <= 0m)
            return 0m;

        var days = RepayIntervalDays * InitialPaymentWeeks;
        var paymentAmount = Math.Round(debt * GetDailyInterestRate(linked) * days, 0, MidpointRounding.AwayFromZero);
        return Math.Max(1m, paymentAmount);
    }

    // 定期返済の最低返済額: 次のうち最も大きい額(残債を超えない)。
    //  - 残債 × MinPaymentRate
    //  - MinPaymentAmount
    //  - 1週間分の利息 + 残債 × MinPrincipalRate(金利を上げても最低返済額で残債が減るようにする。MinPrincipalRate が 0 以下なら適用しない)
    public static decimal CalculateMinimumPayment(decimal debt, decimal principal, bool stopInterest = false, bool linked = false)
    {
        if (debt <= 0m)
            return 0m;

        var minimum = Math.Max(Math.Ceiling(debt * MinPaymentRate), MinPaymentAmount);
        if (MinPrincipalRate > 0m)
        {
            var weeklyInterest = stopInterest ? 0m : CalculateWeeklyInterestAmount(debt, principal, linked);
            var principalPart = Math.Ceiling(debt * MinPrincipalRate);
            minimum = Math.Max(minimum, weeklyInterest + principalPart);
        }

        return Math.Min(debt, minimum);
    }

    // 定期返済で引き落とす額: 支払額(設定)と最低返済額の大きいほう(残債を超えない)。
    private static decimal CalculateScheduledPayment(ServerLoan loan, bool linked)
    {
        var debt = loan.BorrowAmount;
        if (debt <= 0m)
            return 0m;

        var requested = Math.Max(loan.PaymentAmount, CalculateMinimumPayment(debt, loan.Principal, loan.StopInterest, linked));
        return Math.Min(requested, debt);
    }

    // 次回の定期返済の日時(UTC)。スケジューラの実行判定と同じ基準で求める。
    public static DateTime GetNextWeeklyRepayDateTime(DateTime? now = null)
    {
        var baseNow = now ?? DateTime.UtcNow;
        var todayStart = baseNow.Date;
        var daysAhead = ((int)WeeklyRepayDay - (int)baseNow.DayOfWeek + 7) % 7;
        var candidate = todayStart.AddDays(daysAhead).Add(WeeklyRepayTime);
        if (candidate <= baseNow)
        {
            candidate = candidate.AddDays(7);
        }
        return candidate;
    }
}
