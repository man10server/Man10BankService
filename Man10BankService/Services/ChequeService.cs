using System.Threading.Channels;
using Man10BankService.Data;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

public class ChequeService
{
    private readonly IDbContextFactory<BankDbContext> _dbFactory;
    private readonly Channel<Func<Task>> _queue = Channel.CreateUnbounded<Func<Task>>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public ChequeService(IDbContextFactory<BankDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
        _ = Task.Run(WorkerAsync);
    }

    public async Task<ApiResult<Cheque>> CreateAsync(ChequeCreateRequest req)
    {
        return await Enqueue(async () =>
        {
            try
            {
                var repo = new ChequeRepository(_dbFactory);
                var cheque = await repo.CreateChequeAsync(req.Uuid, req.Player, req.Amount, req.Note);
                return ApiResult<Cheque>.Ok(cheque);
            }
            catch (ArgumentException ex)
            {
                return ApiResult<Cheque>.BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return ApiResult<Cheque>.Error($"小切手の作成に失敗しました: {ex.Message}");
            }
        });
    }

    public async Task<ApiResult<Cheque>> UseAsync(int id, ChequeUseRequest req)
    {
        return await Enqueue(async () =>
        {
            try
            {
                var repo = new ChequeRepository(_dbFactory);
                var cheque = await repo.UseChequeAsync(id, req.Player);
                if (cheque == null)
                    return ApiResult<Cheque>.NotFound("小切手が見つかりません。");
                if (cheque.Used && cheque.UsePlayer != req.Player)
                    return ApiResult<Cheque>.Conflict("既に使用済みの小切手です。");
                return ApiResult<Cheque>.Ok(cheque);
            }
            catch (ArgumentException ex)
            {
                return ApiResult<Cheque>.BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return ApiResult<Cheque>.Error($"小切手の使用に失敗しました: {ex.Message}");
            }
        });
    }

    public async Task<ApiResult<Cheque>> GetAsync(int id)
    {
        try
        {
            var repo = new ChequeRepository(_dbFactory);
            var cheque = await repo.GetChequeAsync(id);
            if (cheque == null)
                return ApiResult<Cheque>.NotFound("小切手が見つかりません。");
            return ApiResult<Cheque>.Ok(cheque);
        }
        catch (Exception ex)
        {
            return ApiResult<Cheque>.Error($"小切手の取得に失敗しました: {ex.Message}");
        }
    }
    
    private async Task WorkerAsync()
    {
        await foreach (var job in _queue.Reader.ReadAllAsync())
        {
            try { await job(); }
            catch { /* ジョブ内で完結 */ }
        }
    }

    private Task<T> Enqueue<T>(Func<Task<T>> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Writer.TryWrite(async () =>
        {
            try { tcs.SetResult(await work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }
}
