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
        catch (Exception)
        {
            return ApiResult<decimal>.Error(ErrorCode.UnexpectedError);
        }
    }

    public async Task<ApiResult<List<MoneyLog>>> GetLogsAsync(string uuid, int limit = 100, int offset = 0)
    {
        if (limit is < 1 or > 1000)
            return ApiResult<List<MoneyLog>>.BadRequest(ErrorCode.LimitOutOfRange);
        if (offset < 0)
            return ApiResult<List<MoneyLog>>.BadRequest(ErrorCode.OffsetOutOfRange);
        try
        {
            var repo = new BankRepository(_dbFactory);
            var logs = await repo.GetMoneyLogsAsync(uuid, limit, offset);
            return ApiResult<List<MoneyLog>>.Ok(logs);
        }
        catch (Exception)
        {
            return ApiResult<List<MoneyLog>>.Error(ErrorCode.UnexpectedError);
        }
    }

    public Task<ApiResult<decimal>> DepositAsync(DepositRequest req)
    {
        return Enqueue(async () =>
        {
            try
            {
                var repo = new BankRepository(_dbFactory);
                var player = await MinecraftProfileService.GetNameByUuidAsync(req.Uuid);
                if (player == null)
                {
                    return ApiResult<decimal>.NotFound(ErrorCode.PlayerNotFound);
                }
                var bal = await repo.ChangeBalanceAsync(req.Uuid, player, req.Amount, req.PluginName, req.Note, req.DisplayNote, req.Server);
                return ApiResult<decimal>.Ok(bal);
            }
            catch (ArgumentException)
            {
                return ApiResult<decimal>.BadRequest(ErrorCode.ValidationError);
            }
            catch (Exception)
            {
                return ApiResult<decimal>.Error(ErrorCode.UnexpectedError);
            }
        });
    }

    public Task<ApiResult<decimal>> WithdrawAsync(WithdrawRequest req)
    {
        return Enqueue(async () =>
        {
            try
            {
                var repo = new BankRepository(_dbFactory);
                var current = await repo.GetBalanceAsync(req.Uuid);
                if (current < req.Amount)
                    return ApiResult<decimal>.Conflict(ErrorCode.InsufficientFunds);

                var player = await MinecraftProfileService.GetNameByUuidAsync(req.Uuid);
                if (player == null)
                {
                    return ApiResult<decimal>.NotFound(ErrorCode.PlayerNotFound);
                }
                var bal = await repo.ChangeBalanceAsync(req.Uuid, player, -req.Amount, req.PluginName, req.Note, req.DisplayNote, req.Server);
                return ApiResult<decimal>.Ok(bal);
            }
            catch (ArgumentException)
            {
                return ApiResult<decimal>.BadRequest(ErrorCode.ValidationError);
            }
            catch (Exception)
            {
                return ApiResult<decimal>.Error(ErrorCode.UnexpectedError);
            }
        });
    }

    private Task<ApiResult<decimal>> Enqueue(Func<Task<ApiResult<decimal>>> work)
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
            catch (Exception)
            {
                item.Tcs.SetResult(ApiResult<decimal>.Error(ErrorCode.UnexpectedError));
            }
        }
    }
}
