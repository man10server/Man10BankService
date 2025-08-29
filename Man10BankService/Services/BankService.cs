using System.Collections.Concurrent;
using System.Threading.Channels;
using Man10BankService.Models;
using Man10BankService.Repositories;

namespace Man10BankService.Services;

public class BankService
{
    private readonly BankRepository _repo;

    // UUID ごとに直列実行するためのキュー
    private readonly ConcurrentDictionary<string, Channel<BalanceWorkItemDecimal>> _queues = new();
    private readonly ConcurrentDictionary<string, Task> _workers = new();

    public BankService(BankRepository repo)
    {
        _repo = repo;
    }

    // 残高取得（読み取りのため直列化は不要）
    public async Task<Result<decimal>> GetBalanceAsync(string uuid)
    {
        var (ok, err) = ValidateUuid(uuid);
        if (!ok) return Result<decimal>.Fail(ResultCode.InvalidArgument, err);
        try
        {
            var bal = await _repo.GetBalanceAsync(uuid);
            return Result<decimal>.Ok(bal);
        }
        catch (Exception ex)
        {
            return Result<decimal>.Fail(ResultCode.Error, $"残高取得に失敗しました: {ex.Message}");
        }
    }

    // MoneyLog 取得（読み取りのため直列化は不要）
    public async Task<Result<List<MoneyLog>>> GetLogsAsync(string uuid, int limit = 100, int offset = 0)
    {
        var (ok, err) = ValidateUuid(uuid);
        if (!ok) return Result<List<MoneyLog>>.Fail(ResultCode.InvalidArgument, err);
        if (limit < 1 || limit > 1000)
            return Result<List<MoneyLog>>.Fail(ResultCode.InvalidArgument, "limit は 1..1000 の範囲で指定してください。");
        if (offset < 0)
            return Result<List<MoneyLog>>.Fail(ResultCode.InvalidArgument, "offset は 0 以上で指定してください。");
        try
        {
            var logs = await _repo.GetMoneyLogsAsync(uuid, limit, offset);
            return Result<List<MoneyLog>>.Ok(logs);
        }
        catch (Exception ex)
        {
            return Result<List<MoneyLog>>.Fail(ResultCode.Error, $"ログ取得に失敗しました: {ex.Message}");
        }
    }

    // 入金（直列化して順番に実行）
    public Task<Result<decimal>> DepositAsync(
        string uuid,
        string player,
        decimal amount,
        string pluginName = "api",
        string note = "",
        string displayNote = "",
        string server = "")
    {
        var (ok, err) = ValidateUuid(uuid);
        if (!ok) return Task.FromResult(Result<decimal>.Fail(ResultCode.InvalidArgument, err));
        var (okP, errP) = ValidatePlayer(player);
        if (!okP) return Task.FromResult(Result<decimal>.Fail(ResultCode.InvalidArgument, errP));
        if (amount <= 0m)
            return Task.FromResult(Result<decimal>.Fail(ResultCode.InvalidArgument, "入金額は 0 より大きい必要があります。"));

        return EnqueueBalanceChange(uuid, async () =>
        {
            try
            {
                var bal = await _repo.ChangeBalanceAsync(uuid, player, amount, pluginName, note, displayNote, server);
                return Result<decimal>.Ok(bal);
            }
            catch (ArgumentException ex)
            {
                return Result<decimal>.Fail(ResultCode.InvalidArgument, ex.Message);
            }
            catch (Exception ex)
            {
                return Result<decimal>.Fail(ResultCode.Error, $"入金に失敗しました: {ex.Message}");
            }
        });
    }

    // 出金（直列化して順番に実行）
    public Task<Result<decimal>> WithdrawAsync(
        string uuid,
        string player,
        decimal amount,
        string pluginName = "api",
        string note = "",
        string displayNote = "",
        string server = "")
    {
        var (ok, err) = ValidateUuid(uuid);
        if (!ok) return Task.FromResult(Result<decimal>.Fail(ResultCode.InvalidArgument, err));
        var (okP, errP) = ValidatePlayer(player);
        if (!okP) return Task.FromResult(Result<decimal>.Fail(ResultCode.InvalidArgument, errP));
        if (amount <= 0m)
            return Task.FromResult(Result<decimal>.Fail(ResultCode.InvalidArgument, "出金額は 0 より大きい必要があります。"));

        return EnqueueBalanceChange(uuid, async () =>
        {
            try
            {
                // 直列実行キュー内で残高確認 → 不足なら即エラー
                var current = await _repo.GetBalanceAsync(uuid);
                if (current < amount)
                    return Result<decimal>.Fail(ResultCode.InsufficientFunds, "残高不足のため出金できません。");

                var bal = await _repo.ChangeBalanceAsync(uuid, player, -amount, pluginName, note, displayNote, server);
                return Result<decimal>.Ok(bal);
            }
            catch (ArgumentException ex)
            {
                return Result<decimal>.Fail(ResultCode.InvalidArgument, ex.Message);
            }
            catch (Exception ex)
            {
                return Result<decimal>.Fail(ResultCode.Error, $"出金に失敗しました: {ex.Message}");
            }
        });
    }

    // キュー投入とワーカー起動
    private Task<Result<decimal>> EnqueueBalanceChange(string uuid, Func<Task<Result<decimal>>> work)
    {
        var channel = _queues.GetOrAdd(uuid, _ => Channel.CreateUnbounded<BalanceWorkItemDecimal>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }));

        // ワーカーが起動していなければ起動
        _workers.GetOrAdd(uuid, _ => Task.Run(() => WorkerLoopAsync(uuid, channel)));

        var tcs = new TaskCompletionSource<Result<decimal>>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.Writer.TryWrite(new BalanceWorkItemDecimal(work, tcs));
        return tcs.Task;
    }

    private async Task WorkerLoopAsync(string uuid, Channel<BalanceWorkItemDecimal> channel)
    {
        await foreach (var item in channel.Reader.ReadAllAsync())
        {
            try
            {
                var result = await item.Work().ConfigureAwait(false);
                item.Tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                item.Tcs.SetResult(Result<decimal>.Fail(ResultCode.Error, $"処理中にエラーが発生しました: {ex.Message}"));
            }
        }
        // channel が閉じることは現状ない想定
    }

    private static (bool ok, string error) ValidateUuid(string uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid)) return (false, "UUID は必須です。");
        if (uuid.Length > 36) return (false, "UUID は 36 文字以下で指定してください。");
        return (true, "");
    }

    private static (bool ok, string error) ValidatePlayer(string player)
    {
        if (string.IsNullOrWhiteSpace(player)) return (false, "プレイヤー名は必須です。");
        if (player.Length > 16) return (false, "プレイヤー名は 16 文字以下で指定してください。");
        return (true, "");
    }

    private readonly record struct BalanceWorkItemDecimal(Func<Task<Result<decimal>>> Work, TaskCompletionSource<Result<decimal>> Tcs);
}
