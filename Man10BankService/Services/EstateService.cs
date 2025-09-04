using Man10BankService.Data;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

public class EstateService
{
    private readonly IDbContextFactory<BankDbContext> _dbFactory;
    private readonly BankService _bankService;

    public EstateService(IDbContextFactory<BankDbContext> dbFactory, BankService bankService)
    {
        _dbFactory = dbFactory;
        _bankService = bankService;
    }

    public async Task<ApiResult<Estate?>> GetLatestAsync(string uuid)
    {
        try
        {
            var repo = new EstateRepository(_dbFactory);
            var latest = await repo.GetLatestAsync(uuid);
            if (latest == null) return ApiResult<Estate?>.NotFound("資産データが見つかりません。");
            return ApiResult<Estate?>.Ok(latest);
        }
        catch (Exception ex)
        {
            return ApiResult<Estate?>.Error($"資産データの取得に失敗しました: {ex.Message}");
        }
    }

    public async Task<ApiResult<List<EstateHistory>>> GetHistoryAsync(string uuid, int limit = 100, int offset = 0)
    {
        if (limit is < 1 or > 1000) return ApiResult<List<EstateHistory>>.BadRequest("limit は 1..1000 で指定してください。");
        if (offset < 0) return ApiResult<List<EstateHistory>>.BadRequest("offset は 0 以上で指定してください。");
        try
        {
            var repo = new EstateRepository(_dbFactory);
            var list = await repo.GetHistoryAsync(uuid, limit, offset);
            return ApiResult<List<EstateHistory>>.Ok(list);
        }
        catch (Exception ex)
        {
            return ApiResult<List<EstateHistory>>.Error($"資産履歴の取得に失敗しました: {ex.Message}");
        }
    }

    public async Task<ApiResult<bool>> UpdateSnapshotAsync(string uuid, EstateUpdateRequest request)
    {
        try
        {
            // 現在の最新値（存在しない場合は 0 扱い）
            var repo = new EstateRepository(_dbFactory);
            var current = await repo.GetLatestAsync(uuid);

            // bank 残高
            var bankRes = await _bankService.GetBalanceAsync(uuid);
            if (bankRes.StatusCode != 200)
                return new ApiResult<bool>(bankRes.StatusCode, bankRes.Message);
            var bank = bankRes.Data;

            // player 推定（UserBank から or 現行 or 空）
            var player = await GetPlayerAsync(uuid) ?? current?.Player ?? string.Empty;

            // サーバーローンの残債（BorrowAmount が残債）
            var loanOutstanding = await GetServerLoanOutstandingAsync(uuid);

            // 入力（指定がなければ現行 or 0）
            decimal cash = request.Cash ?? current?.Cash ?? 0m;
            decimal vault = request.Vault ?? current?.Vault ?? 0m;
            decimal estateAmount = request.EstateAmount ?? current?.EstateAmount ?? 0m;
            decimal shop = request.Shop ?? current?.Shop ?? 0m;
            decimal crypto = current?.Crypto ?? 0m; // 今回は入力対象外

            // 合計は素直に合算（ローンは負債として減算）
            decimal total = cash + vault + bank + estateAmount + shop + crypto - loanOutstanding;

            var updated = await repo.AddSnapshotIfChangedAsync(
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

            return ApiResult<bool>.Ok(updated, updated ? "資産を更新しました。" : "変更はありませんでした。");
        }
        catch (ArgumentException ex)
        {
            return ApiResult<bool>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResult<bool>.Error($"資産の更新に失敗しました: {ex.Message}");
        }
    }

    private async Task<string?> GetPlayerAsync(string uuid)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var ub = await db.UserBanks.AsNoTracking().FirstOrDefaultAsync(x => x.Uuid == uuid);
        return ub?.Player;
    }

    private async Task<decimal> GetServerLoanOutstandingAsync(string uuid)
    {
        var repo = new ServerLoanRepository(_dbFactory);
        var loan = await repo.GetByUuidAsync(uuid);
        return loan?.BorrowAmount ?? 0m;
    }
}

