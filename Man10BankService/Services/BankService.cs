using System.Collections.Concurrent;
using System.Threading.Channels;
using Man10BankService.Models;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Repositories;
using Man10BankService.Data;
using Man10BankService.Models.Database;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

public class BankService
{
    private readonly IDbContextFactory<BankDbContext> _dbFactory;
    private readonly Channel<TxWorkItem> _txChannel = Channel.CreateUnbounded<TxWorkItem>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    
    private readonly record struct TxWorkItem(Func<Task<ApiResult<decimal>>> Work, TaskCompletionSource<ApiResult<decimal>> Tcs);

    public BankService(IDbContextFactory<BankDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
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
        catch (Exception ex)
        {
            return ApiResult<decimal>.Error($"残高取得に失敗しました: {ex.Message}");
        }
    }

    public async Task<ApiResult<List<MoneyLog>>> GetLogsAsync(string uuid, int limit = 100, int offset = 0)
    {
        if (limit is < 1 or > 1000)
            return ApiResult<List<MoneyLog>>.BadRequest("limit は 1..1000 の範囲で指定してください。");
        if (offset < 0)
            return ApiResult<List<MoneyLog>>.BadRequest("offset は 0 以上で指定してください。");
        try
        {
            var repo = new BankRepository(_dbFactory);
            var logs = await repo.GetMoneyLogsAsync(uuid, limit, offset);
            return ApiResult<List<MoneyLog>>.Ok(logs);
        }
        catch (Exception ex)
        {
            return ApiResult<List<MoneyLog>>.Error($"ログ取得に失敗しました: {ex.Message}");
        }
    }

    public Task<ApiResult<decimal>> DepositAsync(DepositRequest req)
    {
        return EnqueueBalanceChange(async () =>
        {
            try
            {
                var repo = new BankRepository(_dbFactory);
                var bal = await repo.ChangeBalanceAsync(req.Uuid, req.Player, req.Amount, req.PluginName, req.Note, req.DisplayNote, req.Server);
                return ApiResult<decimal>.Ok(bal);
            }
            catch (ArgumentException ex)
            {
                return ApiResult<decimal>.BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return ApiResult<decimal>.Error($"入金に失敗しました: {ex.Message}");
            }
        });
    }

    public Task<ApiResult<decimal>> WithdrawAsync(WithdrawRequest req)
    {
        return EnqueueBalanceChange(async () =>
        {
            try
            {
                var repo = new BankRepository(_dbFactory);
                var current = await repo.GetBalanceAsync(req.Uuid);
                if (current < req.Amount)
                    return ApiResult<decimal>.Conflict("残高不足のため出金できません。");

                var bal = await repo.ChangeBalanceAsync(req.Uuid, req.Player, -req.Amount, req.PluginName, req.Note, req.DisplayNote, req.Server);
                return ApiResult<decimal>.Ok(bal);
            }
            catch (ArgumentException ex)
            {
                return ApiResult<decimal>.BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return ApiResult<decimal>.Error($"出金に失敗しました: {ex.Message}");
            }
        });
    }

    private Task<ApiResult<decimal>> EnqueueBalanceChange(Func<Task<ApiResult<decimal>>> work)
    {
        var tcs = new TaskCompletionSource<ApiResult<decimal>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _txChannel.Writer.TryWrite(new TxWorkItem(work, tcs));
        return tcs.Task;
    }

    private async Task WorkerLoopAsync()
    {
        await foreach (var item in _txChannel.Reader.ReadAllAsync())
        {
            try
            {
                var result = await item.Work().ConfigureAwait(false);
                item.Tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                item.Tcs.SetResult(ApiResult<decimal>.Error($"処理中にエラーが発生しました: {ex.Message}"));
            }
        }
    }

    // 入出金リクエストの形式検証は Request DTO 側（DataAnnotations）で実施。
    // Service はビジネスルール（残高不足など）のみで判定する。
}
