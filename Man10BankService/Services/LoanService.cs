using Man10BankService.Data;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

public class LoanService(IDbContextFactory<BankDbContext> dbFactory, BankService bank)
{
    public async Task<ApiResult<Loan>> GetByIdAsync(int id)
    {
        try
        {
            var repo = new LoanRepository(dbFactory);
            var loan = await repo.GetByIdAsync(id);
            if (loan == null)
                return ApiResult<Loan>.NotFound("借金データが見つかりません。");
            return ApiResult<Loan>.Ok(loan);
        }
        catch (Exception ex)
        {
            return ApiResult<Loan>.Error($"借金データの取得に失敗しました: {ex.Message}");
        }
    }

    public async Task<ApiResult<List<Loan>>> GetByBorrowerUuidAsync(string borrowUuid, int limit = 100, int offset = 0)
    {
        if (string.IsNullOrWhiteSpace(borrowUuid))
            return ApiResult<List<Loan>>.BadRequest("borrowUuid を指定してください。");
        if (limit is < 1 or > 1000)
            return ApiResult<List<Loan>>.BadRequest("limit は 1..1000 の範囲で指定してください。");
        if (offset < 0)
            return ApiResult<List<Loan>>.BadRequest("offset は 0 以上で指定してください。");

        try
        {
            var repo = new LoanRepository(dbFactory);
            var list = await repo.GetByBorrowerUuidAsync(borrowUuid, limit, offset);
            return ApiResult<List<Loan>>.Ok(list);
        }
        catch (Exception ex)
        {
            return ApiResult<List<Loan>>.Error($"借金一覧の取得に失敗しました: {ex.Message}");
        }
    }

    public async Task<ApiResult<Loan?>> CreateAsync(
        string lendUuid,
        string lendPlayer,
        string borrowUuid,
        string borrowPlayer,
        decimal amount,
        DateTime paybackDate,
        string collateralItem)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(lendUuid) || string.IsNullOrWhiteSpace(borrowUuid))
                return ApiResult<Loan?>.BadRequest("貸手/借手の UUID は必須です。");
            if (lendUuid == borrowUuid)
                return ApiResult<Loan?>.BadRequest("貸手と借手が同一です。");
            if (amount <= 0m)
                return ApiResult<Loan?>.BadRequest("金額は 0 より大きい必要があります。");

            // 1) 貸手から出金
            var w = await bank.WithdrawAsync(new WithdrawRequest
            {
                Uuid = lendUuid,
                Player = lendPlayer,
                Amount = amount,
                PluginName = "user_loan",
                Note = "user_loan_lend_withdraw",
                DisplayNote = "個人間貸付(出金)",
                Server = "system"
            });
            if (w.StatusCode != 200)
                return new ApiResult<Loan?>(w.StatusCode, w.Message);
            
            // 2) レコード作成（失敗時は返金）
            var repo = new LoanRepository(dbFactory);
            try
            {
                var entity = await repo.CreateAsync(lendPlayer, lendUuid, borrowPlayer, borrowUuid, amount, paybackDate, collateralItem);
                
                // 3) 借手へ入金（失敗時はレコード削除と返金）
                var deposit = await bank.DepositAsync(new DepositRequest
                {
                    Uuid = borrowUuid,
                    Player = borrowPlayer,
                    Amount = amount,
                    PluginName = "user_loan",
                    Note = "user_loan_borrow_deposit",
                    DisplayNote = "個人間貸付(入金)",
                    Server = "system"
                });

                if (deposit.StatusCode == 200) return ApiResult<Loan?>.Ok(entity);
                
                await repo.DeleteByIdAsync(entity.Id);
                
                await bank.DepositAsync(new DepositRequest
                {
                    Uuid = lendUuid,
                    Player = lendPlayer,
                    Amount = amount,
                    PluginName = "user_loan",
                    Note = "user_loan_compensate_refund",
                    DisplayNote = "個人間貸付(補償返金)",
                    Server = "system"
                });
                return new ApiResult<Loan?>(deposit.StatusCode, deposit.Message);
            }
            catch (Exception ex)
            {
                // 補償: レコード作成失敗 → 資金移動を戻す
                await bank.DepositAsync(new DepositRequest
                {
                    Uuid = lendUuid,
                    Player = lendPlayer,
                    Amount = amount,
                    PluginName = "user_loan",
                    Note = "user_loan_compensate_refund",
                    DisplayNote = "個人間貸付(補償返金)",
                    Server = "system"
                });
                return ApiResult<Loan?>.Error($"借金レコードの作成に失敗しました: {ex.Message}");
            }
        }
        catch (ArgumentException ex)
        {
            return ApiResult<Loan?>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResult<Loan?>.Error($"借金作成に失敗しました: {ex.Message}");
        }
    }

    public async Task<ApiResult<Loan?>> RepayAsync(int id, string borrowerPlayer, decimal requestAmount)
    {
        try
        {
            if (requestAmount <= 0m)
                return ApiResult<Loan?>.BadRequest("返済金額は 0 より大きい必要があります。");

            var repo = new LoanRepository(dbFactory);
            var loan = await repo.GetByIdAsync(id);
            if (loan == null)
                return ApiResult<Loan?>.NotFound("借金データが見つかりません。");

            var pay = Math.Min(requestAmount, loan.Amount);
            if (pay <= 0m)
                return ApiResult<Loan?>.BadRequest("返済不要です。既に完済しています。");

            // 1) 借手から出金
            var w = await bank.WithdrawAsync(new WithdrawRequest
            {
                Uuid = loan.BorrowUuid,
                Player = borrowerPlayer,
                Amount = pay,
                PluginName = "user_loan",
                Note = "user_loan_repay_withdraw",
                DisplayNote = "個人間貸付 返済(出金)",
                Server = "system"
            });
            if (w.StatusCode != 200)
                return new ApiResult<Loan?>(w.StatusCode, w.Message);

            // 2) 貸手へ入金
            var d = await bank.DepositAsync(new DepositRequest
            {
                Uuid = loan.LendUuid,
                Player = loan.LendPlayer,
                Amount = pay,
                PluginName = "user_loan",
                Note = "user_loan_repay_deposit",
                DisplayNote = "個人間貸付 返済(入金)",
                Server = "system"
            });
            if (d.StatusCode != 200)
            {
                // 補償: 貸手への入金失敗 → 借手に返金
                await bank.DepositAsync(new DepositRequest
                {
                    Uuid = loan.BorrowUuid,
                    Player = borrowerPlayer,
                    Amount = pay,
                    PluginName = "user_loan",
                    Note = "user_loan_compensate_refund",
                    DisplayNote = "個人間貸付(補償返金)",
                    Server = "system"
                });
                return new ApiResult<Loan?>(d.StatusCode, d.Message);
            }

            // 3) レコードの金額更新
            var updated = await repo.AdjustAmountAsync(id, -pay);
            if (updated == null)
            {
                // 補償: レコード更新不可 → 資金移動を戻す
                await bank.WithdrawAsync(new WithdrawRequest
                {
                    Uuid = loan.LendUuid,
                    Player = loan.LendPlayer,
                    Amount = pay,
                    PluginName = "user_loan",
                    Note = "user_loan_compensate_withdraw",
                    DisplayNote = "個人間貸付(補償出金)",
                    Server = "system"
                });
                await bank.DepositAsync(new DepositRequest
                {
                    Uuid = loan.BorrowUuid,
                    Player = borrowerPlayer,
                    Amount = pay,
                    PluginName = "user_loan",
                    Note = "user_loan_compensate_refund",
                    DisplayNote = "個人間貸付(補償返金)",
                    Server = "system"
                });
                return ApiResult<Loan?>.Error("返済レコード更新に失敗しました。");
            }

            return ApiResult<Loan?>.Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return ApiResult<Loan?>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResult<Loan?>.Error($"返済処理に失敗しました: {ex.Message}");
        }
    }
}
