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

    // 冪等キーの保持期間と掃除間隔。再送はタイムアウト直後〜数分以内に起きるため7日あれば十分。
    private static readonly TimeSpan IdempotencyKeyRetention = TimeSpan.FromDays(7);
    private static readonly TimeSpan IdempotencyKeyPruneInterval = TimeSpan.FromHours(1);
    private long _lastPruneTicks;

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

    // プレイヤー名の解決。user_bank に口座があればその名前を使い、口座が無い(初回)場合のみ
    // Mojang へ問い合わせる。直列化ワーカーの外で呼ぶこと:
    // ワーカー内で外部 API を待つと、Mojang の遅延/障害が全金銭操作のキュー詰まりとなり、
    // プラグイン側のタイムアウト(結果不明のまま後からコミットされる)を誘発する。
    // 口座が無く Mojang でも見つからない UUID は PlayerNotFound とし、実在しない口座への入金を防ぐ。
    public async Task<ApiResult<string>> ResolvePlayerNameAsync(string uuid)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var stored = await db.UserBanks
                .AsNoTracking()
                .Where(x => x.Uuid == uuid)
                .Select(x => x.Player)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(stored))
                return ApiResult<string>.Ok(stored);

            var resolved = await _profileService.GetNameByUuidAsync(uuid);
            return resolved == null
                ? ApiResult<string>.Fail(ErrorCode.PlayerNotFound)
                : ApiResult<string>.Ok(resolved);
        }
        catch (Exception)
        {
            return ApiResult<string>.Fail(ErrorCode.UnexpectedError);
        }
    }

    public async Task<ApiResult<decimal>> DepositAsync(DepositRequest req)
    {
        var player = await ResolvePlayerNameAsync(req.Uuid);
        if (!player.IsSuccess)
            return ApiResult<decimal>.Fail(player.Code);

        return await RunMoneyOpAsync(IdempotencyRequest.From(req.RequestId, "deposit", req.Uuid, req.Amount), async db =>
        {
            var bal = await BankRepository.ChangeBalanceCoreAsync(
                db, req.Uuid, player.Data!, req.Amount, req.PluginName, req.Note, req.DisplayNote, req.Server);
            return ApiResult<decimal>.Ok(bal);
        });
    }

    public async Task<ApiResult<decimal>> WithdrawAsync(WithdrawRequest req)
    {
        var player = await ResolvePlayerNameAsync(req.Uuid);
        if (!player.IsSuccess)
            return ApiResult<decimal>.Fail(player.Code);

        return await RunMoneyOpAsync(IdempotencyRequest.From(req.RequestId, "withdraw", req.Uuid, req.Amount), async db =>
        {
            // 行ロック下で残高不足を判定してから減算する。
            var bank = await DbLockHelper.GetUserBankForUpdateAsync(db, req.Uuid);
            var current = bank?.Balance ?? 0m;
            if (current < req.Amount)
                return ApiResult<decimal>.Fail(ErrorCode.InsufficientFunds);

            var bal = await BankRepository.ChangeBalanceCoreAsync(
                db, req.Uuid, player.Data!, -req.Amount, req.PluginName, req.Note, req.DisplayNote, req.Server);
            return ApiResult<decimal>.Ok(bal);
        });
    }

    // 送金: 単一トランザクションで送金元出金+送金先入金+MoneyLog2件。
    // user_bank の行ロックは UUID 昇順で取得しデッドロックを防ぐ。
    // 残高不足は InsufficientFunds(409)、同一UUIDは ValidationError(コントローラ前段でも拒否)。
    public async Task<ApiResult<decimal>> TransferAsync(TransferRequest req)
    {
        if (string.Equals(req.FromUuid, req.ToUuid, StringComparison.OrdinalIgnoreCase))
            return ApiResult<decimal>.Fail(ErrorCode.ValidationError);

        var fromPlayer = await ResolvePlayerNameAsync(req.FromUuid);
        if (!fromPlayer.IsSuccess)
            return ApiResult<decimal>.Fail(fromPlayer.Code);
        var toPlayer = await ResolvePlayerNameAsync(req.ToUuid);
        if (!toPlayer.IsSuccess)
            return ApiResult<decimal>.Fail(toPlayer.Code);

        return await RunMoneyOpAsync(IdempotencyRequest.From(req.RequestId, "transfer", req.FromUuid, req.Amount), async db =>
        {
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
                db, req.FromUuid, fromPlayer.Data!, -req.Amount,
                req.PluginName, req.Note, req.DisplayNote, req.Server);

            // 送金先: 入金ログ
            await BankRepository.ChangeBalanceCoreAsync(
                db, req.ToUuid, toPlayer.Data!, req.Amount,
                req.PluginName, req.Note, req.DisplayNote, req.Server);

            return ApiResult<decimal>.Ok(newFromBalance);
        });
    }

    // 冪等キーの照合に使う要求の要約。同じ requestId は同じ操作・対象・金額でなければならない。
    private sealed record IdempotencyRequest(string RequestId, string Operation, string Uuid, decimal Amount)
    {
        public static IdempotencyRequest? From(string? requestId, string operation, string uuid, decimal amount)
            => string.IsNullOrWhiteSpace(requestId)
                ? null
                : new IdempotencyRequest(requestId.Trim().ToLowerInvariant(), operation, uuid, amount);
    }

    // 冪等キー付きの金銭操作。requestId が無い要求は従来どおり毎回実行する。
    // 同じ requestId が処理済みなら work を実行せず初回の結果(残高)を返す。
    // キーの保存は残高変更と同一トランザクションで行い、「残高は変わったがキーが無い」状態を作らない。
    private async Task<ApiResult<decimal>> RunMoneyOpAsync(
        IdempotencyRequest? idem,
        Func<BankDbContext, Task<ApiResult<decimal>>> work)
    {
        if (idem == null)
            return await RunExclusiveAsync(work);

        var result = await RunExclusiveAsync(async db =>
        {
            var replay = await FindReplayAsync(db, idem);
            if (replay != null)
                return replay;

            var fresh = await work(db);
            if (fresh.IsSuccess)
            {
                db.IdempotencyKeys.Add(new IdempotencyKey
                {
                    RequestId = idem.RequestId,
                    Operation = idem.Operation,
                    Uuid = idem.Uuid,
                    Amount = idem.Amount,
                    ResultBalance = fresh.Data,
                });
            }
            return fresh;
        });

        // 複数インスタンスで同一キーが同時に処理された場合、後着側は主キー重複で失敗(UnexpectedError)する。
        // コミット済みのキーがあれば初回結果を返し、呼び出し側に「二重実行」や「失敗」を見せない。
        if (!result.IsSuccess && result.Code == ErrorCode.UnexpectedError)
        {
            var committed = await FindCommittedReplayAsync(idem);
            if (committed != null)
                return committed;
        }

        if (result.IsSuccess)
            SchedulePruneIfDue();
        return result;
    }

    // 処理済みキーがあれば再送として初回結果を返す。内容が異なる場合はキーの使い回しとして拒否する。
    private static async Task<ApiResult<decimal>?> FindReplayAsync(BankDbContext db, IdempotencyRequest idem)
    {
        var stored = await db.IdempotencyKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RequestId == idem.RequestId);
        if (stored == null)
            return null;

        var matches = stored.Operation == idem.Operation
                      && string.Equals(stored.Uuid, idem.Uuid, StringComparison.OrdinalIgnoreCase)
                      && stored.Amount == idem.Amount;
        return matches
            ? ApiResult<decimal>.Ok(stored.ResultBalance)
            : ApiResult<decimal>.Fail(ErrorCode.IdempotencyKeyMismatch);
    }

    private async Task<ApiResult<decimal>?> FindCommittedReplayAsync(IdempotencyRequest idem)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await FindReplayAsync(db, idem);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // 保持期間を過ぎた冪等キーを1時間に1回だけ、ワーカー上の独立ジョブとして削除する。
    private void SchedulePruneIfDue()
    {
        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastPruneTicks);
        if (now - last < IdempotencyKeyPruneInterval.Ticks)
            return;
        if (Interlocked.CompareExchange(ref _lastPruneTicks, now, last) != last)
            return;

        _txChannel.Writer.TryWrite(async () =>
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var cutoff = DateTime.UtcNow - IdempotencyKeyRetention;
                await db.IdempotencyKeys.Where(x => x.CreatedAt < cutoff).ExecuteDeleteAsync();
            }
            catch (Exception)
            {
                // 掃除の失敗は金銭操作に影響させない。次回の間隔で再試行される。
            }
        });
    }

    // 入口の直列化キューに作業を載せ、単一 DbContext・単一トランザクションで実行する。
    // 成功(IsSuccess)時のみ Commit し、失敗時はロールバックする。
    // 小切手・ローンなど横断的な金銭操作はこのメソッドに自身の処理(残高変更+自リソース更新)を
    // 載せることで、補償 Saga を使わず1トランザクションで原子性を担保する。
    // 外部 API(Mojang 等)の待ちはワーカーを塞ぐため、work の中では行わないこと。
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
