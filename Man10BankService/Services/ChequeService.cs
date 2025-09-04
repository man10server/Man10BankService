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
        Task.Run(WorkerAsync);
    }

    public async Task<ApiResult<Cheque>> CreateAsync(ChequeCreateRequest req)
    {
        return await Enqueue(async () =>
        {
            try
            {
                var player = await MinecraftProfileService.GetNameByUuidAsync(req.Uuid) ?? string.Empty;
                // 先に残高を引き落とし
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
                    return new ApiResult<Cheque>(wres.StatusCode, wres.Message);

                var repo = new ChequeRepository(_dbFactory);
                Cheque cheque;
                try
                {
                    cheque = await repo.CreateChequeAsync(req.Uuid, player, req.Amount, req.Note);
                }
                catch (Exception ex)
                {
                    // 失敗時は補償で返金
                    await _bank.DepositAsync(new DepositRequest
                    {
                        Uuid = req.Uuid,
                        Amount = req.Amount,
                        PluginName = PluginName,
                        Note = "cheque_refund",
                        DisplayNote = "小切手作成失敗の返金",
                        Server = "system"
                    });
                    throw new Exception($"小切手作成に失敗しました: {ex.Message}");
                }
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
                var player = await MinecraftProfileService.GetNameByUuidAsync(req.Uuid) ?? string.Empty;
                var repo = new ChequeRepository(_dbFactory);
                var cheque = await repo.GetChequeAsync(id);
                if (cheque == null)
                    return ApiResult<Cheque>.NotFound("小切手が見つかりません。");
                if (cheque.Used)
                    return ApiResult<Cheque>.Conflict("既に使用済みの小切手です。");

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
                    return new ApiResult<Cheque>(dres.StatusCode, dres.Message);

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
