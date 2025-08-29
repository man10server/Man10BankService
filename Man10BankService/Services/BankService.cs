using System.Collections.Concurrent;
using System.Threading.Channels;
using Man10BankService.Models;
using Man10BankService.Repositories;

namespace Man10BankService.Services;

public class BankService
{
    private readonly BankRepository _repo;

    // UUID ごとに直列実行するためのキュー
    private readonly ConcurrentDictionary<string, Channel<BalanceWorkItem>> _queues = new();
    private readonly ConcurrentDictionary<string, Task> _workers = new();

    public BankService(BankRepository repo)
    {
        _repo = repo;
    }

    // 残高取得（読み取りのため直列化は不要）
    public Task<decimal> GetBalanceAsync(string uuid)
    {
        ValidateUuid(uuid);
        return _repo.GetBalanceAsync(uuid);
    }

    // MoneyLog 取得（読み取りのため直列化は不要）
    public Task<List<MoneyLog>> GetLogsAsync(string uuid, int limit = 100, int offset = 0)
    {
        ValidateUuid(uuid);
        return _repo.GetMoneyLogsAsync(uuid, limit, offset);
    }

    // 入金（直列化して順番に実行）
    public Task<decimal> DepositAsync(
        string uuid,
        string player,
        decimal amount,
        string pluginName = "api",
        string note = "",
        string displayNote = "",
        string server = "")
    {
        ValidateUuid(uuid);
        ValidatePlayer(player);
        if (amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount), "入金額は 0 より大きい必要があります。");

        return EnqueueBalanceChange(uuid, () =>
            _repo.ChangeBalanceAsync(uuid, player, amount, pluginName, note, displayNote, server));
    }

    // 出金（直列化して順番に実行）
    public Task<decimal> WithdrawAsync(
        string uuid,
        string player,
        decimal amount,
        string pluginName = "api",
        string note = "",
        string displayNote = "",
        string server = "")
    {
        ValidateUuid(uuid);
        ValidatePlayer(player);
        if (amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount), "出金額は 0 より大きい必要があります。");

        return EnqueueBalanceChange(uuid, () =>
            _repo.ChangeBalanceAsync(uuid, player, -amount, pluginName, note, displayNote, server));
    }

    // キュー投入とワーカー起動
    private Task<decimal> EnqueueBalanceChange(string uuid, Func<Task<decimal>> work)
    {
        var channel = _queues.GetOrAdd(uuid, _ => Channel.CreateUnbounded<BalanceWorkItem>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }));

        // ワーカーが起動していなければ起動
        _workers.GetOrAdd(uuid, _ => Task.Run(() => WorkerLoopAsync(uuid, channel)));

        var tcs = new TaskCompletionSource<decimal>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.Writer.TryWrite(new BalanceWorkItem(work, tcs));
        return tcs.Task;
    }

    private async Task WorkerLoopAsync(string uuid, Channel<BalanceWorkItem> channel)
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
                item.Tcs.SetException(ex);
            }
        }
        // channel が閉じることは現状ない想定
    }

    private static void ValidateUuid(string uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid))
            throw new ArgumentException("UUID は必須です。", nameof(uuid));
        if (uuid.Length > 36)
            throw new ArgumentOutOfRangeException(nameof(uuid), "UUID は 36 文字以下で指定してください。");
    }

    private static void ValidatePlayer(string player)
    {
        if (string.IsNullOrWhiteSpace(player))
            throw new ArgumentException("プレイヤー名は必須です。", nameof(player));
        if (player.Length > 16)
            throw new ArgumentOutOfRangeException(nameof(player), "プレイヤー名は 16 文字以下で指定してください。");
    }

    private readonly record struct BalanceWorkItem(Func<Task<decimal>> Work, TaskCompletionSource<decimal> Tcs);
}

