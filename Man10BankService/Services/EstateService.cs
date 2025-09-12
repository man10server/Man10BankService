using Man10BankService.Data;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Repositories;
using Microsoft.EntityFrameworkCore;


namespace Man10BankService.Services;

public class EstateService(IDbContextFactory<BankDbContext> dbFactory)
{
    public async Task<ApiResult<Estate?>> GetLatestAsync(string uuid)
    {
        try
        {
            var repo = new EstateRepository(dbFactory);
            var latest = await repo.GetLatestAsync(uuid);
            if (latest == null) return ApiResult<Estate?>.NotFound(ErrorCode.EstateNotFound);
            return ApiResult<Estate?>.Ok(latest);
        }
        catch (Exception)
        {
            return ApiResult<Estate?>.Error(ErrorCode.UnexpectedError);
        }
    }

    public async Task<ApiResult<List<EstateHistory>>> GetHistoryAsync(string uuid, int limit = 100, int offset = 0)
    {
        if (limit is < 1 or > 1000) return ApiResult<List<EstateHistory>>.BadRequest(ErrorCode.LimitOutOfRange);
        if (offset < 0) return ApiResult<List<EstateHistory>>.BadRequest(ErrorCode.OffsetOutOfRange);
        try
        {
            var repo = new EstateRepository(dbFactory);
            var list = await repo.GetHistoryAsync(uuid, limit, offset);
            return ApiResult<List<EstateHistory>>.Ok(list);
        }
        catch (Exception)
        {
            return ApiResult<List<EstateHistory>>.Error(ErrorCode.UnexpectedError);
        }
    }

    public async Task<ApiResult<List<Estate>>> GetRankingAsync(int limit = 100, int offset = 0)
    {
        if (limit is < 1 or > 1000) return ApiResult<List<Estate>>.BadRequest(ErrorCode.LimitOutOfRange);
        if (offset < 0) return ApiResult<List<Estate>>.BadRequest(ErrorCode.OffsetOutOfRange);
        try
        {
            var repo = new EstateRepository(dbFactory);
            var list = await repo.GetRankingAsync(limit, offset);
            return ApiResult<List<Estate>>.Ok(list);
        }
        catch (Exception)
        {
            return ApiResult<List<Estate>>.Error(ErrorCode.UnexpectedError);
        }
    }

    public async Task<ApiResult<bool>> UpdateSnapshotAsync(string uuid, EstateUpdateRequest request)
    {
        try
        {
            // 現在の最新値（存在しない場合は 0 扱い）
            var repo = new EstateRepository(dbFactory);
            var current = await repo.GetLatestAsync(uuid);
            
            // player 推定
            var player = await MinecraftProfileService.GetNameByUuidAsync(uuid) ?? current?.Player ?? string.Empty;

            // サーバーローンの残債（BorrowAmount が残債）
            var loanOutstanding = await GetServerLoanBorrowAmountAsync(uuid);
            
            // 銀行残高
            var bank = await GetBankAsync(uuid);

            // 入力（指定がなければ現行 or 0）
            var cash = request.Cash ?? current?.Cash ?? 0m;
            var vault = request.Vault ?? current?.Vault ?? 0m;
            var estateAmount = request.EstateAmount ?? current?.EstateAmount ?? 0m;
            var shop = request.Shop ?? current?.Shop ?? 0m;
            var crypto = current?.Crypto ?? 0m;

            // 合計は合算（ローンは負債として減算）
            var total = cash + vault + bank + estateAmount + shop + crypto - loanOutstanding;

            var isUpdated = await repo.AddSnapshotIfChangedAsync(
                uuid: uuid,
                player: player,
                vault: vault,
                bank: bank,
                cash: cash,
                estateAmount: estateAmount,
                loan: loanOutstanding,
                shop: shop,
                crypto: crypto,
                total: total);

            return isUpdated
                ? ApiResult<bool>.Ok(true, ErrorCode.EstateUpdated)
                : ApiResult<bool>.Ok(false, ErrorCode.EstateNoChange);
        }
        catch (ArgumentException)
        {
            return ApiResult<bool>.BadRequest(ErrorCode.ValidationError);
        }
        catch (Exception)
        {
            return ApiResult<bool>.Error(ErrorCode.UnexpectedError);
        }
    }

    private async Task<decimal> GetServerLoanBorrowAmountAsync(string uuid)
    {
        var repo = new ServerLoanRepository(dbFactory);
        var loan = await repo.GetByUuidAsync(uuid);
        return loan?.BorrowAmount ?? 0m;
    }

    private async Task<decimal> GetBankAsync(string uuid)
    {
        var repo = new BankRepository(dbFactory);
        return await repo.GetBalanceAsync(uuid);
    }
}
