using Man10BankService.Data;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Models.Responses;
using Man10BankService.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

// 電子マネー(user_vault)のサービス層。
// 書き込みは銀行と同じ直列化キュー(BankService.RunExclusiveAsync)を共用することで、
// 電子マネー⇄銀行を跨ぐ move を 1 トランザクションで原子化する(VaultProvider 7.1)。
// 各書き込みは「コミット後」に IVaultNotifier(VaultWsHub)へ確定残高+version を渡し、
// 在席サーバーへ targeting push する。
public class VaultService
{
    private readonly IDbContextFactory<BankDbContext> _dbFactory;
    private readonly BankService _bankService;
    private readonly IPlayerProfileService _profileService;
    private readonly IVaultNotifier _notifier;
    private readonly ILogger<VaultService>? _logger;

    public VaultService(
        IDbContextFactory<BankDbContext> dbFactory,
        BankService bankService,
        IPlayerProfileService profileService,
        IVaultNotifier notifier,
        ILogger<VaultService>? logger = null)
    {
        _dbFactory = dbFactory;
        _bankService = bankService;
        _profileService = profileService;
        _notifier = notifier;
        _logger = logger;
    }

    // 書き込み1件分の確定結果(API応答と push の双方に使う)。
    private sealed record Mutation(string Uuid, string Player, decimal Balance, long Version);

    public async Task<ApiResult<VaultBalanceResponse>> GetBalanceAsync(string uuid)
    {
        try
        {
            var repo = new VaultRepository(_dbFactory);
            var (balance, version) = await repo.GetBalanceAsync(uuid);
            return ApiResult<VaultBalanceResponse>.Ok(new VaultBalanceResponse(balance, version));
        }
        catch (Exception)
        {
            return ApiResult<VaultBalanceResponse>.Fail(ErrorCode.UnexpectedError);
        }
    }

    public async Task<ApiResult<List<VaultLog>>> GetLogsAsync(string uuid, int limit = 100, int offset = 0)
    {
        if (limit is < 1 or > 1000)
            return ApiResult<List<VaultLog>>.Fail(ErrorCode.LimitOutOfRange);
        if (offset < 0)
            return ApiResult<List<VaultLog>>.Fail(ErrorCode.OffsetOutOfRange);
        try
        {
            var repo = new VaultRepository(_dbFactory);
            var logs = await repo.GetLogsAsync(uuid, limit, offset);
            return ApiResult<List<VaultLog>>.Ok(logs);
        }
        catch (Exception)
        {
            return ApiResult<List<VaultLog>>.Fail(ErrorCode.UnexpectedError);
        }
    }

    // 口座を冪等に作成する(hasAccount/createPlayerAccount 用)。残高変更が無いため push しない。
    public async Task<ApiResult<VaultBalanceResponse>> EnsureAccountAsync(string uuid)
    {
        var res = await _bankService.RunExclusiveAsync(async db =>
        {
            var player = await _profileService.GetNameByUuidAsync(uuid);
            if (player == null)
                return ApiResult<Mutation>.Fail(ErrorCode.PlayerNotFound);

            var vault = await VaultRepository.GetOrCreateForUpdateAsync(db, uuid, player);
            return ApiResult<Mutation>.Ok(new Mutation(vault.Uuid, vault.Player, vault.Balance, vault.Version));
        });
        return ToBalanceResponse(res);
    }

    public async Task<ApiResult<VaultBalanceResponse>> DepositAsync(VaultDepositRequest req)
    {
        var res = await _bankService.RunExclusiveAsync(async db =>
        {
            var player = await _profileService.GetNameByUuidAsync(req.Uuid);
            if (player == null)
                return ApiResult<Mutation>.Fail(ErrorCode.PlayerNotFound);

            var vault = await VaultRepository.ChangeBalanceCoreAsync(
                db, req.Uuid, player, req.Amount, req.PluginName, req.Note, req.DisplayNote, req.Server);
            return ApiResult<Mutation>.Ok(new Mutation(vault.Uuid, vault.Player, vault.Balance, vault.Version));
        });

        await PushIfSuccessAsync(res, "DEPOSIT", req.Server);
        return ToBalanceResponse(res);
    }

    public async Task<ApiResult<VaultBalanceResponse>> WithdrawAsync(VaultWithdrawRequest req)
    {
        var res = await _bankService.RunExclusiveAsync(async db =>
        {
            var player = await _profileService.GetNameByUuidAsync(req.Uuid);
            if (player == null)
                return ApiResult<Mutation>.Fail(ErrorCode.PlayerNotFound);

            // 行ロック下で残高不足を判定してから減算する(作成は ChangeBalanceCoreAsync に一本化する)。
            var existing = await DbLockHelper.GetUserVaultForUpdateAsync(db, req.Uuid);
            if ((existing?.Balance ?? 0m) < req.Amount)
                return ApiResult<Mutation>.Fail(ErrorCode.InsufficientFunds);

            var updated = await VaultRepository.ChangeBalanceCoreAsync(
                db, req.Uuid, player, -req.Amount, req.PluginName, req.Note, req.DisplayNote, req.Server);
            return ApiResult<Mutation>.Ok(new Mutation(updated.Uuid, updated.Player, updated.Balance, updated.Version));
        });

        await PushIfSuccessAsync(res, "WITHDRAW", req.Server);
        return ToBalanceResponse(res);
    }

    // 電子マネー → 電子マネー送金(/pay)。両者を 1 Tx で更新し、両者へ push する。
    public async Task<ApiResult<VaultBalanceResponse>> TransferAsync(VaultTransferRequest req)
    {
        Mutation? toMutation = null;

        var res = await _bankService.RunExclusiveAsync(async db =>
        {
            if (string.Equals(req.FromUuid, req.ToUuid, StringComparison.OrdinalIgnoreCase))
                return ApiResult<Mutation>.Fail(ErrorCode.ValidationError);

            var fromPlayer = await _profileService.GetNameByUuidAsync(req.FromUuid);
            if (fromPlayer == null)
                return ApiResult<Mutation>.Fail(ErrorCode.PlayerNotFound);
            var toPlayer = await _profileService.GetNameByUuidAsync(req.ToUuid);
            if (toPlayer == null)
                return ApiResult<Mutation>.Fail(ErrorCode.PlayerNotFound);

            // デッドロック防止のため UUID 昇順で行ロックを取得する(作成は ChangeBalanceCoreAsync に一本化する)。
            var fromFirst = string.CompareOrdinal(req.FromUuid, req.ToUuid) <= 0;
            if (fromFirst)
            {
                await DbLockHelper.GetUserVaultForUpdateAsync(db, req.FromUuid);
                await DbLockHelper.GetUserVaultForUpdateAsync(db, req.ToUuid);
            }
            else
            {
                await DbLockHelper.GetUserVaultForUpdateAsync(db, req.ToUuid);
                await DbLockHelper.GetUserVaultForUpdateAsync(db, req.FromUuid);
            }

            // 送金元の残高不足チェック(ロック取得後の最新値で判定)
            var fromVault = await DbLockHelper.GetUserVaultForUpdateAsync(db, req.FromUuid);
            if ((fromVault?.Balance ?? 0m) < req.Amount)
                return ApiResult<Mutation>.Fail(ErrorCode.InsufficientFunds);

            var newFrom = await VaultRepository.ChangeBalanceCoreAsync(
                db, req.FromUuid, fromPlayer, -req.Amount, req.PluginName, req.Note, req.DisplayNote, req.Server);
            var newTo = await VaultRepository.ChangeBalanceCoreAsync(
                db, req.ToUuid, toPlayer, req.Amount, req.PluginName, req.Note, req.DisplayNote, req.Server);

            toMutation = new Mutation(newTo.Uuid, newTo.Player, newTo.Balance, newTo.Version);
            return ApiResult<Mutation>.Ok(new Mutation(newFrom.Uuid, newFrom.Player, newFrom.Balance, newFrom.Version));
        });

        if (res.IsSuccess)
        {
            await PushIfSuccessAsync(res, "TRANSFER", req.Server);
            if (toMutation != null)
                await PushAsync(toMutation, "TRANSFER", req.Server);
        }
        return ToBalanceResponse(res);
    }

    // 管理用: 絶対値設定。オフライン(在席不明)プレイヤーを変更できる唯一の経路(VaultProvider 4.5)。
    public async Task<ApiResult<VaultBalanceResponse>> SetAsync(VaultSetRequest req)
    {
        var changedFlag = false;

        var res = await _bankService.RunExclusiveAsync(async db =>
        {
            var player = await _profileService.GetNameByUuidAsync(req.Uuid);
            if (player == null)
                return ApiResult<Mutation>.Fail(ErrorCode.PlayerNotFound);

            var (vault, changed) = await VaultRepository.SetBalanceCoreAsync(
                db, req.Uuid, player, req.Amount, req.PluginName, req.Note, req.DisplayNote, req.Server);
            changedFlag = changed;
            return ApiResult<Mutation>.Ok(new Mutation(vault.Uuid, vault.Player, vault.Balance, vault.Version));
        });

        // 残高に変化があった時のみ push する。
        if (res.IsSuccess && changedFlag)
            await PushIfSuccessAsync(res, "SET", req.Server);
        return ToBalanceResponse(res);
    }

    // 電子マネー ⇄ 銀行残高を 1 Tx で移動する(VaultProvider 7.2)。
    // ロックは固定順(user_vault → user_bank)で取得する。
    public async Task<ApiResult<VaultMoveResponse>> MoveAsync(VaultMoveRequest req)
    {
        var res = await _bankService.RunExclusiveAsync(async db =>
        {
            var player = await _profileService.GetNameByUuidAsync(req.Uuid);
            if (player == null)
                return ApiResult<VaultMoveResponse>.Fail(ErrorCode.PlayerNotFound);

            // 固定順(user_vault → user_bank)で両行をロックする(作成は各 ChangeBalanceCoreAsync に一本化する)。
            var vault = await DbLockHelper.GetUserVaultForUpdateAsync(db, req.Uuid);
            var bank = await DbLockHelper.GetUserBankForUpdateAsync(db, req.Uuid);
            var vaultBalance = vault?.Balance ?? 0m;
            var bankBalance = bank?.Balance ?? 0m;

            decimal vaultDelta;
            decimal bankDelta;
            if (req.Direction == VaultMoveDirection.VaultToBank)
            {
                if (vaultBalance < req.Amount)
                    return ApiResult<VaultMoveResponse>.Fail(ErrorCode.InsufficientFunds);
                vaultDelta = -req.Amount;
                bankDelta = req.Amount;
            }
            else
            {
                if (bankBalance < req.Amount)
                    return ApiResult<VaultMoveResponse>.Fail(ErrorCode.InsufficientFunds);
                vaultDelta = req.Amount;
                bankDelta = -req.Amount;
            }

            var newVault = await VaultRepository.ChangeBalanceCoreAsync(
                db, req.Uuid, player, vaultDelta, req.PluginName, req.Note, req.DisplayNote, req.Server);
            var newBankBalance = await BankRepository.ChangeBalanceCoreAsync(
                db, req.Uuid, player, bankDelta, req.PluginName, req.Note, req.DisplayNote, req.Server);

            return ApiResult<VaultMoveResponse>.Ok(
                new VaultMoveResponse(newVault.Balance, newBankBalance, newVault.Version));
        });

        if (res.IsSuccess && res.Data != null)
        {
            await PushAsync(
                new Mutation(req.Uuid, "", res.Data.VaultBalance, res.Data.VaultVersion),
                "BANK_MOVE", req.Server);
        }
        return res;
    }

    private async Task PushIfSuccessAsync(ApiResult<Mutation> res, string cause, string originServer)
    {
        if (res.IsSuccess && res.Data != null)
            await PushAsync(res.Data, cause, originServer);
    }

    private async Task PushAsync(Mutation m, string cause, string originServer)
    {
        // push 失敗は DB コミット済みの操作結果に影響させない(自己修復は次の resync / push で行う)。
        try
        {
            await _notifier.PushBalanceAsync(
                new VaultBalanceChange(m.Uuid, m.Balance, m.Version, cause, originServer));
        }
        catch (Exception e)
        {
            _logger?.LogWarning(e, "残高変更 push に失敗しました uuid={Uuid} cause={Cause}", m.Uuid, cause);
        }
    }

    private static ApiResult<VaultBalanceResponse> ToBalanceResponse(ApiResult<Mutation> res)
    {
        return res.IsSuccess && res.Data != null
            ? ApiResult<VaultBalanceResponse>.Ok(new VaultBalanceResponse(res.Data.Balance, res.Data.Version))
            : ApiResult<VaultBalanceResponse>.Fail(res.Code);
    }
}
