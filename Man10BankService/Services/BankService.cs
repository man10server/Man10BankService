using System.Threading.Channels;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Repositories;
using Man10BankService.Data;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

public class BankService
{
    private readonly IDbContextFactory<BankDbContext> _dbFactory;
    private readonly IPlayerProfileService _profileService;

    // 入口の直列化キュー。deposit/withdraw/transfer や小切手・ローンなど
    // user_bank を更新する全トランザクションをこのワーカーで直列実行する。
    // 正しさの根拠は各トランザクション内の行ロック(SELECT ... FOR UPDATE)へ移ったが、
    // 単一プロセス前提のホットパス競合を減らすため入口の直列化は維持する。
    private readonly Channel<Func<Task>> _txChannel = Channel.CreateUnbounded<Func<Task>>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public BankService(IDbContextFactory<BankDbContext> dbFactory, IPlayerProfileService profileService)
    {
        _dbFactory = dbFactory;
        _profileService = profileService;
        Task.Run(WorkerLoopAsync);
    }

    public async Task<ApiResult<decimal>> GetBalanceAsync(string uuid)
    {
        try
        {
            var repo = new BankRepository(_dbFactory);
            var bal = await repo.GetBalanceAsync(uuid);
            return ApiResult<decimal>.Ok(bal);
        }
        catch (Exception)
        {
            return ApiResult<decimal>.Fail(ErrorCode.UnexpectedError);
        }
    }

    public async Task<ApiResult<List<MoneyLog>>> GetLogsAsync(string uuid, int limit = 100, int offset = 0)
    {
        if (limit is < 1 or > 1000)
            return ApiResult<List<MoneyLog>>.Fail(ErrorCode.LimitOutOfRange);
        if (offset < 0)
            return ApiResult<List<MoneyLog>>.Fail(ErrorCode.OffsetOutOfRange);
        try
        {
            var repo = new BankRepository(_dbFactory);
            var logs = await repo.GetMoneyLogsAsync(uuid, limit, offset);
            return ApiResult<List<MoneyLog>>.Ok(logs);
        }
        catch (Exception)
        {
            return ApiResult<List<MoneyLog>>.Fail(ErrorCode.UnexpectedError);
        }
    }

    public Task<ApiResult<decimal>> DepositAsync(DepositRequest req)
    {
        return RunExclusiveAsync(async db =>
        {
            var player = await _profileService.GetNameByUuidAsync(req.Uuid);
            if (player == null)
                return ApiResult<decimal>.Fail(ErrorCode.PlayerNotFound);

            var bal = await BankRepository.ChangeBalanceCoreAsync(
                db, req.Uuid, player, req.Amount, req.PluginName, req.Note, req.DisplayNote, req.Server);
            return ApiResult<decimal>.Ok(bal);
        });
    }

    public Task<ApiResult<decimal>> WithdrawAsync(WithdrawRequest req)
    {
        return RunExclusiveAsync(async db =>
        {
            var player = await _profileService.GetNameByUuidAsync(req.Uuid);
            if (player == null)
                return ApiResult<decimal>.Fail(ErrorCode.PlayerNotFound);

            // 行ロック下で残高不足を判定してから減算する。
            var bank = await DbLockHelper.GetUserBankForUpdateAsync(db, req.Uuid);
            var current = bank?.Balance ?? 0m;
            if (current < req.Amount)
                return ApiResult<decimal>.Fail(ErrorCode.InsufficientFunds);

            var bal = await BankRepository.ChangeBalanceCoreAsync(
                db, req.Uuid, player, -req.Amount, req.PluginName, req.Note, req.DisplayNote, req.Server);
            return ApiResult<decimal>.Ok(bal);
        });
    }

    // 送金: 単一トランザクションで送金元出金+送金先入金+MoneyLog2件。
    // user_bank の行ロックは UUID 昇順で取得しデッドロックを防ぐ。
    // 残高不足は InsufficientFunds(409)、同一UUIDは ValidationError(コントローラ前段でも拒否)。
    public Task<ApiResult<decimal>> TransferAsync(TransferRequest req)
    {
        return RunExclusiveAsync(async db =>
        {
            if (string.Equals(req.FromUuid, req.ToUuid, StringComparison.OrdinalIgnoreCase))
                return ApiResult<decimal>.Fail(ErrorCode.ValidationError);

            var fromPlayer = await _profileService.GetNameByUuidAsync(req.FromUuid);
            if (fromPlayer == null)
                return ApiResult<decimal>.Fail(ErrorCode.PlayerNotFound);
            var toPlayer = await _profileService.GetNameByUuidAsync(req.ToUuid);
            if (toPlayer == null)
                return ApiResult<decimal>.Fail(ErrorCode.PlayerNotFound);

            // デッドロック防止のため UUID 昇順で行ロックを取得する。
            var ordered = string.CompareOrdinal(req.FromUuid, req.ToUuid) <= 0
                ? new[] { req.FromUuid, req.ToUuid }
                : new[] { req.ToUuid, req.FromUuid };
            foreach (var uuid in ordered)
                await DbLockHelper.GetUserBankForUpdateAsync(db, uuid);

            // 送金元の残高不足チェック(ロック取得後の最新値で判定)
            var fromBank = await db.UserBanks.FirstOrDefaultAsync(x => x.Uuid == req.FromUuid);
            var fromBalance = fromBank?.Balance ?? 0m;
            if (fromBalance < req.Amount)
                return ApiResult<decimal>.Fail(ErrorCode.InsufficientFunds);

            // 送金元: 出金ログ
            var newFromBalance = await BankRepository.ChangeBalanceCoreAsync(
                db, req.FromUuid, fromPlayer, -req.Amount,
                req.PluginName, req.Note, req.DisplayNote, req.Server);

            // 送金先: 入金ログ
            await BankRepository.ChangeBalanceCoreAsync(
                db, req.ToUuid, toPlayer, req.Amount,
                req.PluginName, req.Note, req.DisplayNote, req.Server);

            return ApiResult<decimal>.Ok(newFromBalance);
        });
    }

    // 入口の直列化キューに作業を載せ、単一 DbContext・単一トランザクションで実行する。
    // 成功(IsSuccess)時のみ Commit し、失敗時はロールバックする。
    // 小切手・ローンなど横断的な金銭操作はこのメソッドに自身の処理(残高変更+自リソース更新)を
    // 載せることで、補償 Saga を使わず1トランザクションで原子性を担保する。
    public Task<ApiResult<T>> RunExclusiveAsync<T>(Func<BankDbContext, Task<ApiResult<T>>> work)
    {
        var tcs = new TaskCompletionSource<ApiResult<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _txChannel.Writer.TryWrite(async () =>
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                await using var tx = await db.Database.BeginTransactionAsync();

                ApiResult<T> result;
                try
                {
                    result = await work(db);
                }
                catch (ArgumentException)
                {
                    tcs.SetResult(ApiResult<T>.Fail(ErrorCode.ValidationError));
                    return;
                }

                if (result.IsSuccess)
                {
                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                }
                // 失敗時は using による Dispose でロールバックされる。

                tcs.SetResult(result);
            }
            catch (Exception)
            {
                tcs.SetResult(ApiResult<T>.Fail(ErrorCode.UnexpectedError));
            }
        });
        return tcs.Task;
    }

    private async Task WorkerLoopAsync()
    {
        await foreach (var job in _txChannel.Reader.ReadAllAsync())
        {
            try { await job(); }
            catch { /* ジョブ内で TaskCompletionSource を完了させている */ }
        }
    }
}
