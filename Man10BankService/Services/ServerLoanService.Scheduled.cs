using Man10BankService.Data;
using Man10BankService.Models.Database;
using Man10BankService.Repositories;

namespace Man10BankService.Services;

// スケジューラ(ServerLoanSchedulerService)が動かす処理。
//  - 毎日:    全契約に利息を付け(RunDailyInterestForAllAsync)、続けて延滞分を回収する(RunOverdueCollectionForAllAsync)。
//  - 週 1 回: 全契約の定期返済を行う(RunWeeklyRepayForAllAsync)。
// どれも契約 1 件ごとに別のトランザクションで処理する。1 件が失敗しても、ほかの契約の処理は続ける。
public partial class ServerLoanService
{
    // ===== 全契約への実行 =====

    // 日次利息を全契約へ加算する(スケジューラから毎日呼ばれる)。today を省略すると今日(UTC)の分として付ける。
    public async Task RunDailyInterestForAllAsync(DateOnly? today = null)
    {
        var date = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var repo = new ServerLoanRepository(_dbFactory, _profileService);
        var loans = await repo.GetAllAsync();
        foreach (var loan in loans)
        {
            await AddDailyInterestAsync(loan.Uuid, date);
        }
    }

    // 定期返済を全契約に実行する(スケジューラから週 1 回呼ばれる)。
    public async Task RunWeeklyRepayForAllAsync()
    {
        var repo = new ServerLoanRepository(_dbFactory, _profileService);
        var loans = await repo.GetAllAsync();
        foreach (var loan in loans)
        {
            await RepayScheduledAsync(loan.Uuid);
        }
    }

    // 延滞中の契約から延滞分を回収する(スケジューラから毎日、利息の加算に続けて呼ばれる)。
    // 回収は未回収分までしか行わないため、同日に複数回実行されても二重回収にならない。
    public async Task RunOverdueCollectionForAllAsync()
    {
        var repo = new ServerLoanRepository(_dbFactory, _profileService);
        var loans = await repo.GetAllAsync();
        foreach (var loan in loans)
        {
            if (loan.BorrowAmount <= 0m)
                continue;
            await CollectOverdueAsync(loan.Uuid);
        }
    }

    // ===== 契約 1 件分の処理 =====

    // 日次利息(契約 1 件分): 残債に 1 日分の利息を加え、Interest として記録する。
    //  - date の分の Interest ログがすでにある契約には付けない(同じ日に 2 回実行されても二重に付かない)。
    //  - 残債が 0 の契約と、利息停止(stop_interest)の契約には付けない。
    // 戻り値は、利息を付けたかどうか。
    private async Task<ApiResult<bool>> AddDailyInterestAsync(string ownerUuid, DateOnly date)
    {
        return await _bank.RunExclusiveAsync<bool>(async db =>
        {
            var loan = await DbLockHelper.GetServerLoanForUpdateAsync(db, ownerUuid);
            if (loan == null || loan.StopInterest)
                return ApiResult<bool>.Ok(false);
            if (await ServerLoanRepository.HasInterestLogOnDateAsync(db, ownerUuid, date))
                return ApiResult<bool>.Ok(false);

            var interest = CalculateDailyInterestAmount(loan.BorrowAmount, await ServerLoanRepository.HasMembersAsync(db, ownerUuid));
            if (interest <= 0m)
                return ApiResult<bool>.Ok(false);

            ServerLoanRepository.AdjustLoanCore(db, loan, interest, ServerLoanRepository.ServerLoanLogAction.Interest);
            return ApiResult<bool>.Ok(true);
        });
    }

    // 定期返済(契約 1 件分): 支払額(設定)と最低返済額の大きいほうを、契約の全アカウントの口座から引き落とす。
    //  - 口座は本垢→参加が古い順に使う。全員の残高の合計で足りなければ、どの口座からも引き落とさず失敗とする。
    //  - 成功は RepaySuccess、失敗は RepayFailure として記録する。この 2 つが借入枠の増減と延滞の判定に使われる。
    //  - 延滞後に手動返済で完済したまま返済日を迎えた契約は、0 円の成功を記録して延滞を解消する。
    // 戻り値は、ログを記録したかどうか。
    private async Task<ApiResult<bool>> RepayScheduledAsync(string ownerUuid)
    {
        return await _bank.RunExclusiveAsync<bool>(async db =>
        {
            // 1) server_loan 行ロック
            var loan = await DbLockHelper.GetServerLoanForUpdateAsync(db, ownerUuid);
            if (loan == null)
                return ApiResult<bool>.Ok(false);

            var debt = loan.BorrowAmount;
            if (debt <= 0m)
            {
                // 延滞後に手動返済で完済した契約だけ、0 円の成功を記録して延滞を解消する。それ以外は何もしない。
                if (!await ServerLoanRepository.IsLatestScheduledRepayFailureAsync(db, ownerUuid))
                    return ApiResult<bool>.Ok(false);

                ServerLoanRepository.AdjustLoanCore(db, loan, 0m, ServerLoanRepository.ServerLoanLogAction.RepaySuccess);
                return ApiResult<bool>.Ok(true);
            }

            var amount = CalculateScheduledPayment(debt, loan.PaymentAmount, loan.StopInterest, await ServerLoanRepository.HasMembersAsync(db, ownerUuid));
            // 最低返済額の設定をすべて 0 にしていて支払額も未設定のときだけ 0 になる。引き落とすものが無いので何もしない。
            if (amount <= 0m)
                return ApiResult<bool>.Ok(false);

            // 2) 契約の全アカウントの口座を行ロックし、合計残高で残高不足を判定
            var accounts = await LockBankAccountsAsync(db, await ServerLoanRepository.GetContractAccountUuidsAsync(db, ownerUuid));
            if (TotalBalance(accounts) < amount)
            {
                // 残債は変えず、引き落とそうとした額を失敗として記録する(これで延滞になる)。
                ServerLoanRepository.AdjustLoanCore(db, loan, amount, ServerLoanRepository.ServerLoanLogAction.RepayFailure);
                return ApiResult<bool>.Ok(true);
            }

            // 3) 残高減算 + MoneyLog
            await WithdrawFromAccountsAsync(db, accounts, amount, "loan_repay", "サーバーローン返済");

            // 4) 債務減算 + 返済ログ
            ServerLoanRepository.AdjustLoanCore(db, loan, -amount, ServerLoanRepository.ServerLoanLogAction.RepaySuccess);
            return ApiResult<bool>.Ok(true);
        });
    }

    // 延滞分の回収: 直近の定期返済が失敗のままの契約について、未回収分を銀行残高のある分だけ回収する。
    // 契約の全アカウント(本垢→参加が古い順)の口座から回収する。
    // 失敗額を回収しきるか残債が 0 になったら、0 円の成功ログを残して延滞を解消する(借入枠は増えない)。
    // 戻り値は、ログを記録したかどうか。
    private async Task<ApiResult<bool>> CollectOverdueAsync(string ownerUuid)
    {
        return await _bank.RunExclusiveAsync<bool>(async db =>
        {
            var loan = await DbLockHelper.GetServerLoanForUpdateAsync(db, ownerUuid);
            if (loan == null || loan.BorrowAmount <= 0m)
                return ApiResult<bool>.Ok(false);

            // 延滞中でなければ(直近の定期返済が失敗でなければ)回収するものは無い。
            var uncollected = await ServerLoanRepository.GetUncollectedOverdueAmountAsync(db, ownerUuid);
            if (uncollected is null)
                return ApiResult<bool>.Ok(false);

            // 回収したい額(target)のうち、全員の口座にある分(amount)だけ回収する。
            var accounts = await LockBankAccountsAsync(db, await ServerLoanRepository.GetContractAccountUuidsAsync(db, ownerUuid));
            var target = Math.Min(uncollected.Value, loan.BorrowAmount);
            var amount = Math.Min(target, TotalBalance(accounts));
            if (target > 0m && amount <= 0m)
                return ApiResult<bool>.Ok(false);

            if (amount > 0m)
            {
                await WithdrawFromAccountsAsync(db, accounts, amount, "loan_overdue_collect", "サーバーローン延滞分の回収");
                ServerLoanRepository.AdjustLoanCore(db, loan, -amount, ServerLoanRepository.ServerLoanLogAction.OverdueCollect);
            }

            // 回収しきったら延滞を解消する。
            if (amount >= target)
                ServerLoanRepository.AdjustLoanCore(db, loan, 0m, ServerLoanRepository.ServerLoanLogAction.RepaySuccess);

            return ApiResult<bool>.Ok(true);
        });
    }

    // ===== 口座からの引き落とし =====

    // 口座を順に行ロックして読む(MySQL 時 FOR UPDATE)。口座が無いアカウントは含めない。
    private static async Task<List<UserBank>> LockBankAccountsAsync(BankDbContext db, IEnumerable<string> uuids)
    {
        var accounts = new List<UserBank>();
        foreach (var uuid in uuids)
        {
            var account = await DbLockHelper.GetUserBankForUpdateAsync(db, uuid);
            if (account != null)
                accounts.Add(account);
        }

        return accounts;
    }

    // 引き落とせる残高の合計。
    private static decimal TotalBalance(IEnumerable<UserBank> accounts)
    {
        return accounts.Sum(x => Math.Max(0m, x.Balance));
    }

    // 先頭の口座から順に、合計が amount になるまで引き落とす(残高 + MoneyLog。SaveChanges は呼び出し側)。
    // 呼び出し前に、口座の合計残高が amount 以上であることを確かめておくこと。
    private async Task WithdrawFromAccountsAsync(
        BankDbContext db, IEnumerable<UserBank> accounts, decimal amount, string note, string displayNote)
    {
        var remaining = amount;
        foreach (var account in accounts)
        {
            if (remaining <= 0m)
                break;

            var withdraw = Math.Min(account.Balance, remaining);
            if (withdraw <= 0m)
                continue;

            // 入出金ログには名簿の最新の名前を残す。名簿に無ければ口座の名前を使う。
            var player = await _profileService.GetNameByUuidAsync(account.Uuid) ?? account.Player;
            await BankRepository.ChangeBalanceCoreAsync(
                db, account.Uuid, player, -withdraw,
                "server_loan", note, displayNote, "system");
            remaining -= withdraw;
        }
    }
}
