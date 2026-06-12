using Man10BankService.Data;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

public class ChequeService
{
    private readonly IDbContextFactory<BankDbContext> _dbFactory;
    private readonly BankService _bank;
    private readonly IPlayerProfileService _profileService;

    private const string PluginName = "Man10Bank";

    public ChequeService(IDbContextFactory<BankDbContext> dbFactory, BankService bank, IPlayerProfileService profileService)
    {
        _dbFactory = dbFactory;
        _bank = bank;
        _profileService = profileService;
    }

    // 小切手発行: 単一トランザクションで「発行者残高減算(op時はスキップ)+MoneyLog→小切手INSERT→Commit」。
    // BankService の直列化キュー上で実行し、補償(返金)Saga を廃止する。
    public async Task<ApiResult<Cheque>> CreateAsync(ChequeCreateRequest req)
    {
        var player = await _profileService.GetNameByUuidAsync(req.Uuid);
        if (player == null)
            return ApiResult<Cheque>.Fail(ErrorCode.PlayerNotFound);

        return await _bank.RunExclusiveAsync<Cheque>(async db =>
        {
            // op が false の場合のみ残高を引き落とす(行ロック下で残高不足を判定)。
            if (!req.Op)
            {
                var bank = await DbLockHelper.GetUserBankForUpdateAsync(db, req.Uuid);
                var balance = bank?.Balance ?? 0m;
                if (balance < req.Amount)
                    return ApiResult<Cheque>.Fail(ErrorCode.InsufficientFunds);

                await BankRepository.ChangeBalanceCoreAsync(
                    db, req.Uuid, player, -req.Amount,
                    PluginName, $"create_cheque: {req.Note}", $"小切手作成: {req.Note}", "system");
            }

            var cheque = ChequeRepository.AddChequeCore(db, req.Uuid, player, req.Amount, req.Note, req.Op);
            return ApiResult<Cheque>.Ok(cheque);
        });
    }

    // 小切手使用: 単一トランザクションで「小切手行ロック→未使用確認→Used更新→受取人残高加算+MoneyLog→Commit」。
    // ロック順序: 自リソース(cheque)行→user_bank 行。入金とUsed更新の原子性を保証する。
    public async Task<ApiResult<Cheque>> UseAsync(int id, ChequeUseRequest req)
    {
        var player = await _profileService.GetNameByUuidAsync(req.Uuid);
        if (player == null)
            return ApiResult<Cheque>.Fail(ErrorCode.PlayerNotFound);

        return await _bank.RunExclusiveAsync<Cheque>(async db =>
        {
            // 1) 小切手行ロック
            var cheque = await ChequeRepository.GetChequeForUpdateAsync(db, id);
            if (cheque == null)
                return ApiResult<Cheque>.Fail(ErrorCode.ChequeNotFound);
            if (cheque.Used)
                return ApiResult<Cheque>.Fail(ErrorCode.ChequeAlreadyUsed);

            // 2) Used 更新
            cheque.Used = true;
            cheque.UsePlayer = player;
            cheque.UseDate = DateTime.UtcNow;

            // 3) 受取人残高加算 + MoneyLog(同一 tx)
            await BankRepository.ChangeBalanceCoreAsync(
                db, req.Uuid, player, cheque.Amount,
                PluginName, $"cheque_use:{id}", "小切手使用", "system");

            return ApiResult<Cheque>.Ok(cheque);
        });
    }

    public async Task<ApiResult<Cheque>> GetAsync(int id)
    {
        try
        {
            var repo = new ChequeRepository(_dbFactory);
            var cheque = await repo.GetChequeAsync(id);
            if (cheque == null)
                return ApiResult<Cheque>.Fail(ErrorCode.ChequeNotFound);
            return ApiResult<Cheque>.Ok(cheque);
        }
        catch (Exception)
        {
            return ApiResult<Cheque>.Fail(ErrorCode.UnexpectedError);
        }
    }
}
