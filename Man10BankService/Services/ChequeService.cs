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
    private readonly BankService _bank;
    private readonly Channel<Func<Task>> _queue = Channel.CreateUnbounded<Func<Task>>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    
    private const string PluginName = "Man10Bank";

    public ChequeService(IDbContextFactory<BankDbContext> dbFactory, BankService bank)
    {
        _dbFactory = dbFactory;
        _bank = bank;
        Task.Run(WorkerLoopAsync);
    }

    public async Task<ApiResult<Cheque>> CreateAsync(ChequeCreateRequest req)
    {
        return await Enqueue(async () =>
        {
            try
            {
                var player = await MinecraftProfileService.GetNameByUuidAsync(req.Uuid);
                if (player == null)
                {
                    return ApiResult<Cheque>.NotFound(ErrorCode.PlayerNotFound);
                }
                var withdrew = false;
                // Op が false の場合のみ残高を引き落とし
                if (!req.Op)
                {
                    var wres = await _bank.WithdrawAsync(new WithdrawRequest
                    {
                        Uuid = req.Uuid,
                        Amount = req.Amount,
                        PluginName = PluginName,
                        Note = $"create_cheque: {req.Note}",
                        DisplayNote = $"小切手作成: {req.Note}",
                        Server = "system"
                    });
                    if (wres.StatusCode != 200)
                        return new ApiResult<Cheque>(wres.StatusCode, wres.Code);
                    withdrew = true;
                }

                var repo = new ChequeRepository(_dbFactory);
                Cheque cheque;
                try
                {
                    cheque = await repo.CreateChequeAsync(req.Uuid, player, req.Amount, req.Note, req.Op);
                }
                catch (Exception)
                {
                    // 失敗時は補償で返金（引き落としている場合のみ）
                    if (withdrew)
                    {
                        await _bank.DepositAsync(new DepositRequest
                        {
                            Uuid = req.Uuid,
                            Amount = req.Amount,
                            PluginName = PluginName,
                            Note = "cheque_refund",
                            DisplayNote = "小切手作成失敗の返金",
                            Server = "system"
                        });
                    }
                    throw new Exception("create_cheque_failed");
                }
                return ApiResult<Cheque>.Ok(cheque);
            }
            catch (ArgumentException)
            {
                return ApiResult<Cheque>.BadRequest(ErrorCode.ValidationError);
            }
            catch (Exception)
            {
                return ApiResult<Cheque>.Error(ErrorCode.UnexpectedError);
            }
        });
    }

    public async Task<ApiResult<Cheque>> UseAsync(int id, ChequeUseRequest req)
    {
        return await Enqueue(async () =>
        {
            try
            {
                var player = await MinecraftProfileService.GetNameByUuidAsync(req.Uuid);
                if (player == null)
                {
                    return ApiResult<Cheque>.NotFound(ErrorCode.PlayerNotFound);
                }
                var repo = new ChequeRepository(_dbFactory);
                var cheque = await repo.GetChequeAsync(id);
                if (cheque == null)
                    return ApiResult<Cheque>.NotFound(ErrorCode.ChequeNotFound);
                if (cheque.Used)
                    return ApiResult<Cheque>.Conflict(ErrorCode.ChequeAlreadyUsed);

                // 先に使用済みへ更新（更新失敗時はここで終了し入金しない）
                cheque = await repo.UseChequeAsync(id, player) ?? cheque;

                // 次に入金
                var dres = await _bank.DepositAsync(new DepositRequest
                {
                    Uuid = req.Uuid,
                    Amount = cheque.Amount,
                    PluginName = PluginName,
                    Note = $"cheque_use:{id}",
                    DisplayNote = "小切手使用",
                    Server = "system"
                });
                if (dres.StatusCode != 200)
                    return new ApiResult<Cheque>(dres.StatusCode, dres.Code);

                return ApiResult<Cheque>.Ok(cheque);
            }
            catch (ArgumentException)
            {
                return ApiResult<Cheque>.BadRequest(ErrorCode.ValidationError);
            }
            catch (Exception)
            {
                return ApiResult<Cheque>.Error(ErrorCode.UnexpectedError);
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
                return ApiResult<Cheque>.NotFound(ErrorCode.ChequeNotFound);
            return ApiResult<Cheque>.Ok(cheque);
        }
        catch (Exception)
        {
            return ApiResult<Cheque>.Error(ErrorCode.UnexpectedError);
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
    
    private async Task WorkerLoopAsync()
    {
        await foreach (var job in _queue.Reader.ReadAllAsync())
        {
            try { await job(); }
            catch { /* ジョブ内で完結 */ }
        }
    }
}
