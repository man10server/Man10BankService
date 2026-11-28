using Man10BankService.Models.Database;
using Man10BankService.Repositories;

namespace Man10BankService.Services;

// 借入枠の計算。枠は DB に保存せず、契約のログ(server_loan_contract_log)を古い順にたどって毎回求める。
// 期日どおりの定期返済を重ねると増え、定期返済に失敗すると減る。手動返済や延滞分の回収では変わらない。
public partial class ServerLoanService
{
    // 借入枠の算定で参照するログの種類(server_loan_contract_log.action の値)
    private const string RepaySuccessAction = nameof(ServerLoanRepository.ServerLoanLogAction.RepaySuccess);
    private const string RepayFailureAction = nameof(ServerLoanRepository.ServerLoanLogAction.RepayFailure);
    private const string InterestAction = nameof(ServerLoanRepository.ServerLoanLogAction.Interest);
    private const string MemberJoinAction = nameof(ServerLoanRepository.ServerLoanLogAction.MemberJoin);
    private const string MemberLeaveAction = nameof(ServerLoanRepository.ServerLoanLogAction.MemberLeave);
    private const string LimitResetAction = nameof(ServerLoanRepository.ServerLoanLogAction.LimitReset);

    // 借入枠を、契約の全ログ(古い順)をたどって求める。枠は DB に保存せず、毎回ここで計算する。
    // currentDebt は契約の現在の残債で、各ログの時点の残債を逆算するために使う。
    //  - MinAmount から始める。
    //  - 定期返済の成功: CalculateGrowth の額だけ増やす。
    //  - 定期返済の失敗: FailureCutRate を掛けて減らす。
    //  - 参加・脱退: 参加者がいる間だけ枠の上限が LinkedMaxAmount に切り替わる。
    //  - 枠の初期化(運営による強制リンク時の罰則): MinAmount に戻す。以後の成功でまた増える。
    //  - 手動返済・延滞分の回収・借入などは枠に影響しない。
    private static decimal CalculateBorrowLimit(IReadOnlyList<ServerLoanLog> logs, decimal currentDebt)
    {
        var debtBeforeLog = CalculateDebtBeforeEachLog(logs, currentDebt);

        // その時点で契約に参加者がいたかどうか(参加・脱退のログから数える)。枠の上限の切り替えに使う。
        var members = 0;
        var limit = ClampBorrowLimit(MinAmount, linked: false);
        // 前回の定期返済より後に、利息が付いた時点の残債(平均残債の材料)
        var interestDebts = new List<(DateTime Date, decimal Debt)>();

        for (var i = 0; i < logs.Count; i++)
        {
            var log = logs[i];
            switch (log.Action)
            {
                case InterestAction:
                    interestDebts.Add((log.Date, Math.Max(0m, debtBeforeLog[i])));
                    break;

                case RepayFailureAction:
                    limit = ClampBorrowLimit(limit * FailureCutRate, members > 0);
                    interestDebts.Clear();
                    break;

                // 参加者の有無で枠の上限が変わるので、その時点の上限に収め直す。
                case MemberJoinAction:
                    members++;
                    limit = ClampBorrowLimit(limit, members > 0);
                    break;

                case MemberLeaveAction:
                    members = Math.Max(0, members - 1);
                    limit = ClampBorrowLimit(limit, members > 0);
                    break;

                case LimitResetAction:
                    limit = ClampBorrowLimit(MinAmount, members > 0);
                    break;

                // 0 円の成功は延滞解消の記録なので、枠を増やさず、その週の利息の記録も消費しない。
                case RepaySuccessAction when log.Amount != 0m:
                    // 返済ログの金額は残債の増減(負数)で記録されるため、絶対値が返済額になる。
                    var paid = Math.Abs(log.Amount);
                    var averageDebt = CalculateAverageDebt(interestDebts, log.Date);
                    limit = ClampBorrowLimit(limit + CalculateGrowth(limit, paid, averageDebt), members > 0);
                    interestDebts.Clear();
                    break;
            }
        }

        return limit;
    }

    // 各ログが記録される直前の残債を、現在の残債からログの増減を新しい順に巻き戻して求める。
    private static decimal[] CalculateDebtBeforeEachLog(IReadOnlyList<ServerLoanLog> logs, decimal currentDebt)
    {
        var debtBeforeLog = new decimal[logs.Count];
        var debt = currentDebt;
        for (var i = logs.Count - 1; i >= 0; i--)
        {
            // 返済失敗ログの金額は引き落とそうとした額で、残債は変わっていない。
            if (logs[i].Action != RepayFailureAction)
                debt -= logs[i].Amount;
            debtBeforeLog[i] = debt;
        }

        return debtBeforeLog;
    }

    // その週の平均残債: 定期返済の直近 RepayIntervalDays 日以内に利息が付いた時点の残債の合計 ÷ RepayIntervalDays。
    // 利息が付かなかった日は 0 として数えるため、返済の直前だけ借りても平均残債は増えない。
    private static decimal CalculateAverageDebt(IEnumerable<(DateTime Date, decimal Debt)> interestDebts, DateTime repayDate)
    {
        var from = repayDate.AddDays(-RepayIntervalDays);
        return interestDebts.Where(x => x.Date > from).Sum(x => x.Debt) / RepayIntervalDays;
    }

    // 定期返済の成功 1 回で増える枠: 「返済額」と「その週の平均残債」の小さいほう × GrowthFactor。
    // ただし、その時点の枠 × MaxGrowthRate を上限とする。
    private static decimal CalculateGrowth(decimal limit, decimal paid, decimal averageDebt)
    {
        var growth = Math.Min(paid, averageDebt) * GrowthFactor;
        var maxGrowth = limit * MaxGrowthRate;
        return Math.Min(growth, maxGrowth);
    }

    // 借入枠を MinAmount 〜 上限の範囲に収め、1 円未満を切り捨てる。
    // 上限は、参加者がいる契約(linked)なら LinkedMaxAmount(未設定なら MaxAmount)、いなければ MaxAmount。
    private static decimal ClampBorrowLimit(decimal limit, bool linked)
    {
        var maxAmount = linked ? LinkedMaxAmount ?? MaxAmount : MaxAmount;
        limit = Math.Max(MinAmount, limit);
        limit = Math.Min(maxAmount, limit);
        return Math.Max(0m, Math.Floor(limit));
    }
}
