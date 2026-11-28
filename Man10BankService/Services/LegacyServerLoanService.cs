using Man10BankService.Data;
using Man10BankService.Models.Database;
using Man10BankService.Models.Responses;
using Man10BankService.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

// 旧リボ: 0.5.0 までのサーバーローンで借りたまま残っている残債を、返し終わるまで管理する。
// 新リボ(ServerLoanService)とは別の仕組みで、テーブルも当時のもの(server_loan_tbl / server_loan_log)を使う。
//
// 決まり
//  - 対象は、server_loan_tbl に残債が 1 円以上残っているプレイヤーだけ。
//  - 利息は付けない。新しく借りることもできない。
//  - 旧リボの残債が残っている間は、新リボを借りられない(判定は ServerLoanService.BorrowAsync)。
//    完済して 0 円になると、以後そのプレイヤーの操作は新リボに対するものになる。
//  - 定期返済は新リボと同じ曜日・時刻に行い、支払額(設定)どおりに本人の口座から引き落とす。
//    新リボと違い、最低返済額・延滞の制限・借入枠は無い。残高が足りなければ失敗を記録するだけ。
//
// 全員が完済したら、このクラスと LegacyServerLoanRepository・LegacyServerLoan(Log)、
// および呼び出し元(ServerLoanController の振り分け、ServerLoanService.BorrowAsync の判定、
// ServerLoanSchedulerService、EstateService)の該当箇所を削除する。
//
// 書き込みの決まりは新リボと同じ: BankService.RunExclusiveAsync の中で、旧リボの行 → 口座の順に行ロックする。
public class LegacyServerLoanService(
    IDbContextFactory<BankDbContext> dbFactory, BankService bank, IPlayerProfileService profileService)
{
    private const string MoneyLogPlugin = "server_loan";
    private const string MoneyLogNote = "legacy_loan_repay";
    private const string MoneyLogDisplayNote = "旧リボ返済";

    // ===== 参照 =====

    // uuid に旧リボの残債が残っているかどうか。
    public Task<ApiResult<bool>> HasDebtAsync(string uuid)
    {
        return ReadAsync(async db =>
            ApiResult<bool>.Ok(await LegacyServerLoanRepository.FindActiveAsync(db, uuid) != null));
    }

    // uuid の旧リボ。残債が残っていなければ LoanNotFound。
    public Task<ApiResult<LegacyServerLoan>> GetAsync(string uuid)
    {
        return ReadAsync(async db =>
        {
            var loan = await LegacyServerLoanRepository.FindActiveAsync(db, uuid);
            return loan == null
                ? ApiResult<LegacyServerLoan>.Fail(ErrorCode.LoanNotFound)
                : ApiResult<LegacyServerLoan>.Ok(loan);
        });
    }

    // /mrevo に表示する返済情報: 次回の定期返済の日時と、そのとき引き落とす額。利息は付かないので 0。
    public async Task<ApiResult<PaymentInfoResponse>> GetPaymentInfoAsync(string uuid)
    {
        var loanRes = await GetAsync(uuid);
        if (!loanRes.IsSuccess)
            return ApiResult<PaymentInfoResponse>.Fail(loanRes.Code);

        return ApiResult<PaymentInfoResponse>.Ok(new PaymentInfoResponse(
            ServerLoanService.GetNextWeeklyRepayDateTime(), 0m, CalculateScheduledPayment(loanRes.Data!)));
    }

    // uuid の旧リボのログ(新しい順)。
    public async Task<ApiResult<List<LegacyServerLoanLog>>> GetLogsAsync(string uuid, int limit = 100, int offset = 0)
    {
        if (limit is < 1 or > 1000)
            return ApiResult<List<LegacyServerLoanLog>>.Fail(ErrorCode.LimitOutOfRange);
        if (offset < 0)
            return ApiResult<List<LegacyServerLoanLog>>.Fail(ErrorCode.OffsetOutOfRange);

        return await ReadAsync(async db =>
            ApiResult<List<LegacyServerLoanLog>>.Ok(await LegacyServerLoanRepository.GetLogsAsync(db, uuid, limit, offset)));
    }

    private async Task<ApiResult<T>> ReadAsync<T>(Func<BankDbContext, Task<ApiResult<T>>> read)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            return await read(db);
        }
        catch (Exception)
        {
            return ApiResult<T>.Fail(ErrorCode.UnexpectedError);
        }
    }

    // ===== プレイヤーの操作 =====

    // 手動返済: プレイヤーが自分の口座から返す(残債を超える分は返さない)。
    public async Task<ApiResult<LegacyServerLoan>> RepayAsync(string uuid, decimal amount)
    {
        if (amount <= 0m)
            return ApiResult<LegacyServerLoan>.Fail(ErrorCode.ValidationError);

        var resolved = await bank.ResolvePlayerNameAsync(uuid);
        if (!resolved.IsSuccess)
            return ApiResult<LegacyServerLoan>.Fail(resolved.Code);

        return await bank.RunExclusiveAsync<LegacyServerLoan>(async db =>
        {
            var loan = await DbLockHelper.GetLegacyServerLoanForUpdateAsync(db, uuid);
            if (loan == null)
                return ApiResult<LegacyServerLoan>.Fail(ErrorCode.NoRepaymentNeeded);

            var pay = Math.Min(amount, loan.BorrowAmount);
            var account = await DbLockHelper.GetUserBankForUpdateAsync(db, uuid);
            if ((account?.Balance ?? 0m) < pay)
                return ApiResult<LegacyServerLoan>.Fail(ErrorCode.InsufficientFunds);

            await BankRepository.ChangeBalanceCoreAsync(
                db, uuid, resolved.Data!, -pay, MoneyLogPlugin, MoneyLogNote, MoneyLogDisplayNote, "system");
            LegacyServerLoanRepository.Adjust(db, loan, -pay, LegacyServerLoanRepository.LogAction.EarlyRepay);
            return ApiResult<LegacyServerLoan>.Ok(loan);
        });
    }

    // 支払額(設定)の変更。定期返済で引き落とす額を決める。
    public async Task<ApiResult<LegacyServerLoan>> SetPaymentAmountAsync(string uuid, decimal paymentAmount)
    {
        if (paymentAmount < 0m)
            return ApiResult<LegacyServerLoan>.Fail(ErrorCode.ValidationError);

        return await bank.RunExclusiveAsync<LegacyServerLoan>(async db =>
        {
            var loan = await DbLockHelper.GetLegacyServerLoanForUpdateAsync(db, uuid);
            if (loan == null)
                return ApiResult<LegacyServerLoan>.Fail(ErrorCode.LoanNotFound);

            loan.PaymentAmount = paymentAmount;
            return ApiResult<LegacyServerLoan>.Ok(loan);
        });
    }

    // ===== 運営の操作 =====

    // 運営による残債の上書き。差額を SetBorrowAmount として記録する。支払額(設定)は変えない。
    // 0 円にすると旧リボは無くなり、以後そのプレイヤーは新リボを使う。
    public async Task<ApiResult<LegacyServerLoan>> SetBorrowAmountAsync(string uuid, decimal amount)
    {
        if (amount < 0m)
            return ApiResult<LegacyServerLoan>.Fail(ErrorCode.BorrowAmountMustBeZeroOrGreater);

        return await bank.RunExclusiveAsync<LegacyServerLoan>(async db =>
        {
            var loan = await DbLockHelper.GetLegacyServerLoanForUpdateAsync(db, uuid);
            if (loan == null)
                return ApiResult<LegacyServerLoan>.Fail(ErrorCode.LoanNotFound);

            LegacyServerLoanRepository.Adjust(db, loan, amount - loan.BorrowAmount, LegacyServerLoanRepository.LogAction.SetBorrowAmount);
            return ApiResult<LegacyServerLoan>.Ok(loan);
        });
    }

    // ===== 定期返済 =====

    // 旧リボの残債が残っている全員の定期返済を行う(スケジューラから週 1 回呼ばれる)。
    // 1 件ごとに別のトランザクションで処理する。1 件が失敗しても、ほかの人の処理は続ける。
    public async Task RunWeeklyRepayForAllAsync()
    {
        List<LegacyServerLoan> loans;
        await using (var db = await dbFactory.CreateDbContextAsync())
            loans = await LegacyServerLoanRepository.GetAllActiveAsync(db);

        foreach (var loan in loans)
        {
            await RepayScheduledAsync(loan.Uuid);
        }
    }

    // 定期返済(1 人分): 支払額(設定)を本人の口座から引き落とす(残債を超える分は引き落とさない)。
    //  - 支払額(設定)が 0 以下なら何もしない。
    //  - 残高が足りなければ、引き落とさずに失敗(RepayFailure)を記録する。新リボと違い、延滞の制限は掛からない。
    // 戻り値は、ログを記録したかどうか。
    private async Task<ApiResult<bool>> RepayScheduledAsync(string uuid)
    {
        return await bank.RunExclusiveAsync<bool>(async db =>
        {
            var loan = await DbLockHelper.GetLegacyServerLoanForUpdateAsync(db, uuid);
            if (loan == null)
                return ApiResult<bool>.Ok(false);

            var amount = CalculateScheduledPayment(loan);
            if (amount <= 0m)
                return ApiResult<bool>.Ok(false);

            var account = await DbLockHelper.GetUserBankForUpdateAsync(db, uuid);
            if ((account?.Balance ?? 0m) < amount)
            {
                LegacyServerLoanRepository.Adjust(db, loan, amount, LegacyServerLoanRepository.LogAction.RepayFailure);
                return ApiResult<bool>.Ok(true);
            }

            // 入出金ログには名簿の最新の名前を残す。名簿に無ければ旧リボの行の名前を使う。
            var player = await profileService.GetNameByUuidAsync(uuid) ?? loan.Player;
            await BankRepository.ChangeBalanceCoreAsync(
                db, uuid, player, -amount, MoneyLogPlugin, MoneyLogNote, MoneyLogDisplayNote, "system");
            LegacyServerLoanRepository.Adjust(db, loan, -amount, LegacyServerLoanRepository.LogAction.RepaySuccess);
            return ApiResult<bool>.Ok(true);
        });
    }

    // 定期返済で引き落とす額: 支払額(設定)。残債を超えない。未設定(0 以下)なら 0。
    private static decimal CalculateScheduledPayment(LegacyServerLoan loan)
    {
        if (loan.PaymentAmount <= 0m)
            return 0m;
        return Math.Min(loan.PaymentAmount, loan.BorrowAmount);
    }
}
