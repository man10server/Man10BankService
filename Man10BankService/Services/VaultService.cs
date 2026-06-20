using Man10BankService.Data;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Models.Responses;
using Man10BankService.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Man10BankService.Services;

// 電子マネー(user_vault)の権威更新を担うサービス。
// 書き込みは既存 BankService.RunExclusiveAsync 上で実行し、move を user_vault <-> user_bank の 1 Tx 化する。
// コミット後に IVaultNotifier で Provider キャッシュ収束用の push を行う。
public class VaultService
{
    private readonly BankService _bankService;
    private readonly VaultRepository _vaultRepo;
    private readonly IPlayerProfileService _profileService;
    private readonly IVaultNotifier _notifier;
    private readonly VaultOptions _options;
    private readonly ILogger<VaultService> _logger;

    public VaultService(
        BankService bankService,
        IDbContextFactory<BankDbContext> dbFactory,
        IPlayerProfileService profileService,
        IVaultNotifier notifier,
        IOptions<VaultOptions> options,
        ILogger<VaultService> logger)
    {
        _bankService = bankService;
        _vaultRepo = new VaultRepository(dbFactory);
        _profileService = profileService;
        _notifier = notifier;
        _options = options.Value;
        _logger = logger;
    }

    // ===================== 読み取り =====================

    public async Task<ApiResult<VaultBalanceData>> GetBalanceAsync(string uuid)
    {
        try
        {
            var data = await _vaultRepo.GetBalanceAsync(uuid);
            return ApiResult<VaultBalanceData>.Ok(data);
        }
        catch (Exception)
        {
            return ApiResult<VaultBalanceData>.Fail(ErrorCode.UnexpectedError);
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
            var logs = await _vaultRepo.GetLogsAsync(uuid, limit, offset);
            return ApiResult<List<VaultLog>>.Ok(logs);
        }
        catch (Exception)
        {
            return ApiResult<List<VaultLog>>.Fail(ErrorCode.UnexpectedError);
        }
    }

    // Vault 設定の配布。MaxBalance が不正なら fail-closed(VaultConfigInvalid)。
    public ApiResult<VaultConfigResponse> GetConfig()
    {
        if (!_options.IsMaxBalanceValid)
        {
            _logger.LogError("Vault:MaxBalance の設定値が不正です(value={Value})。Vault API を fail-closed で無効化します。", _options.MaxBalance);
            return ApiResult<VaultConfigResponse>.Fail(ErrorCode.VaultConfigInvalid);
        }

        return ApiResult<VaultConfigResponse>.Ok(new VaultConfigResponse(
            _options.MaxBalance,
            _options.JoinReadyDelayMillis,
            _options.QuitDrainTimeoutMillis));
    }

    // ===================== 書き込み =====================

    public Task<ApiResult<VaultBalanceData>> DepositAsync(VaultDepositRequest req)
    {
        if (!_options.IsMaxBalanceValid)
            return Task.FromResult(ApiResult<VaultBalanceData>.Fail(ErrorCode.VaultConfigInvalid));

        var source = req.Source ?? VaultSource.MAN10_API;
        return RunVaultWriteAsync(req.Uuid, "DEPOSIT", req.OperationId, req.Server, async db =>
        {
            // 冪等再送: 既に適用済みなら現在の権威残高を返す(再照会)。
            var replay = await TryReplayAsync(db, req.Uuid, req.OperationId);
            if (replay != null) return replay;

            var player = await ResolvePlayerAsync(db, req.Uuid);
            if (player == null) return ApiResult<VaultBalanceData>.Fail(ErrorCode.PlayerNotFound);

            // 残高を増やす操作: 行ロックを取得してから更新後残高の上限を検証する(正しさの根拠を行ロックに置く)。
            var locked = await DbLockHelper.GetUserVaultForUpdateAsync(db, req.Uuid);
            var current = locked?.Balance ?? 0m;
            if (req.Amount > _options.MaxBalance || current + req.Amount > _options.MaxBalance)
                return ApiResult<VaultBalanceData>.Fail(ErrorCode.BalanceLimitExceeded);

            var data = await VaultRepository.ChangeBalanceCoreAsync(
                db, req.Uuid, player, req.Amount,
                req.PluginName, req.Note, req.DisplayNote, req.Server, source, req.OperationId);
            return ApiResult<VaultBalanceData>.Ok(data);
        });
    }

    public Task<ApiResult<VaultBalanceData>> WithdrawAsync(VaultWithdrawRequest req)
    {
        if (!_options.IsMaxBalanceValid)
            return Task.FromResult(ApiResult<VaultBalanceData>.Fail(ErrorCode.VaultConfigInvalid));

        var source = req.Source ?? VaultSource.MAN10_API;
        return RunVaultWriteAsync(req.Uuid, "WITHDRAW", req.OperationId, req.Server, async db =>
        {
            var replay = await TryReplayAsync(db, req.Uuid, req.OperationId);
            if (replay != null) return replay;

            var player = await ResolvePlayerAsync(db, req.Uuid);
            if (player == null) return ApiResult<VaultBalanceData>.Fail(ErrorCode.PlayerNotFound);

            // 行ロック下で残高不足を判定してから減算する。
            var vault = await DbLockHelper.GetUserVaultForUpdateAsync(db, req.Uuid);
            var current = vault?.Balance ?? 0m;
            if (current < req.Amount)
                return ApiResult<VaultBalanceData>.Fail(ErrorCode.InsufficientFunds);

            var data = await VaultRepository.ChangeBalanceCoreAsync(
                db, req.Uuid, player, -req.Amount,
                req.PluginName, req.Note, req.DisplayNote, req.Server, source, req.OperationId);
            return ApiResult<VaultBalanceData>.Ok(data);
        });
    }

    // 絶対値設定(管理者)。在席状況を問わず受理する。source は ADMIN。
    public Task<ApiResult<VaultBalanceData>> SetAsync(VaultSetRequest req)
    {
        if (!_options.IsMaxBalanceValid)
            return Task.FromResult(ApiResult<VaultBalanceData>.Fail(ErrorCode.VaultConfigInvalid));

        return RunVaultWriteAsync(req.Uuid, "SET", req.OperationId, req.Server, async db =>
        {
            var replay = await TryReplayAsync(db, req.Uuid, req.OperationId);
            if (replay != null) return replay;

            var player = await ResolvePlayerAsync(db, req.Uuid);
            if (player == null) return ApiResult<VaultBalanceData>.Fail(ErrorCode.PlayerNotFound);

            // 絶対値設定は更新後残高(=設定値)も上限検証する。
            if (req.Amount > _options.MaxBalance)
                return ApiResult<VaultBalanceData>.Fail(ErrorCode.BalanceLimitExceeded);

            var data = await VaultRepository.SetBalanceCoreAsync(
                db, req.Uuid, player, req.Amount,
                req.PluginName, req.Note, req.DisplayNote, req.Server, VaultSource.ADMIN, req.OperationId);
            return ApiResult<VaultBalanceData>.Ok(data);
        });
    }

    // 電子マネー送金(/pay)。送金元出金 + 送金先入金 + vault_log 2件を 1 Tx で行う。
    // user_vault の行ロックは UUID 昇順で取得しデッドロックを防ぐ。
    public async Task<ApiResult<VaultTransferData>> TransferAsync(VaultTransferRequest req)
    {
        if (!_options.IsMaxBalanceValid)
            return ApiResult<VaultTransferData>.Fail(ErrorCode.VaultConfigInvalid);

        var res = await _bankService.RunExclusiveAsync<VaultTransferData>(async db =>
        {
            if (string.Equals(req.FromUuid, req.ToUuid, StringComparison.OrdinalIgnoreCase))
                return ApiResult<VaultTransferData>.Fail(ErrorCode.ValidationError);

            // 冪等再送: 既に適用済みなら双方の現在残高を返す(再照会)。
            var existing = await VaultRepository.FindLogByOperationIdAsync(db, req.OperationId);
            if (existing != null)
            {
                var f = await _vaultRepo.GetBalanceAsync(req.FromUuid);
                var t = await _vaultRepo.GetBalanceAsync(req.ToUuid);
                return ApiResult<VaultTransferData>.Ok(new VaultTransferData(f, t));
            }

            var fromPlayer = await ResolvePlayerAsync(db, req.FromUuid);
            if (fromPlayer == null) return ApiResult<VaultTransferData>.Fail(ErrorCode.PlayerNotFound);
            var toPlayer = await ResolvePlayerAsync(db, req.ToUuid);
            if (toPlayer == null) return ApiResult<VaultTransferData>.Fail(ErrorCode.PlayerNotFound);

            // デッドロック防止のため UUID 昇順で行ロックを取得する。
            var ordered = string.CompareOrdinal(req.FromUuid, req.ToUuid) <= 0
                ? new[] { req.FromUuid, req.ToUuid }
                : new[] { req.ToUuid, req.FromUuid };
            foreach (var uuid in ordered)
                await DbLockHelper.GetUserVaultForUpdateAsync(db, uuid);

            var fromVault = await db.UserVaults.FirstOrDefaultAsync(x => x.Uuid == req.FromUuid);
            var fromBalance = fromVault?.Balance ?? 0m;
            if (fromBalance < req.Amount)
                return ApiResult<VaultTransferData>.Fail(ErrorCode.InsufficientFunds);

            var toVault = await db.UserVaults.FirstOrDefaultAsync(x => x.Uuid == req.ToUuid);
            var toBalance = toVault?.Balance ?? 0m;
            // 受取人の更新後残高が上限を超える場合は拒否する。
            if (req.Amount > _options.MaxBalance || toBalance + req.Amount > _options.MaxBalance)
                return ApiResult<VaultTransferData>.Fail(ErrorCode.BalanceLimitExceeded);

            // 送金元: 冪等キーは送金元行に付与する。
            var fromData = await VaultRepository.ChangeBalanceCoreAsync(
                db, req.FromUuid, fromPlayer, -req.Amount,
                req.PluginName, req.Note, req.DisplayNote, req.Server, VaultSource.MAN10_API, req.OperationId);

            // 送金先: 冪等キーは付与しない(operation_id UNIQUE のため 1 操作に 1 件)。
            var toData = await VaultRepository.ChangeBalanceCoreAsync(
                db, req.ToUuid, toPlayer, req.Amount,
                req.PluginName, req.Note, req.DisplayNote, req.Server, VaultSource.MAN10_API, null);

            return ApiResult<VaultTransferData>.Ok(new VaultTransferData(fromData, toData));
        });

        if (res.IsSuccess && res.Data != null)
        {
            await SafePushAsync(new VaultBalanceChange(req.FromUuid, res.Data.From.Balance, res.Data.From.Version, "TRANSFER", req.OperationId, req.Server));
            await SafePushAsync(new VaultBalanceChange(req.ToUuid, res.Data.To.Balance, res.Data.To.Version, "TRANSFER", null, req.Server));
        }

        return res;
    }

    // user_vault <-> user_bank の移動(/deposit /withdraw)。
    // user_vault 行ロック -> user_bank 行ロックの順で 1 Tx に統一する。bank 更新は既存 BankRepository を使う。
    public async Task<ApiResult<VaultMoveData>> MoveAsync(VaultMoveRequest req)
    {
        if (!_options.IsMaxBalanceValid)
            return ApiResult<VaultMoveData>.Fail(ErrorCode.VaultConfigInvalid);

        var res = await _bankService.RunExclusiveAsync<VaultMoveData>(async db =>
        {
            var existing = await VaultRepository.FindLogByOperationIdAsync(db, req.OperationId);
            if (existing != null)
            {
                var vb = await _vaultRepo.GetBalanceAsync(req.Uuid);
                var bb = (await db.UserBanks.AsNoTracking().FirstOrDefaultAsync(x => x.Uuid == req.Uuid))?.Balance ?? 0m;
                return ApiResult<VaultMoveData>.Ok(new VaultMoveData(vb.Balance, vb.Version, bb));
            }

            var player = await ResolvePlayerAsync(db, req.Uuid);
            if (player == null) return ApiResult<VaultMoveData>.Fail(ErrorCode.PlayerNotFound);

            // ロック順序: user_vault -> user_bank。
            var vault = await DbLockHelper.GetUserVaultForUpdateAsync(db, req.Uuid);
            await DbLockHelper.GetUserBankForUpdateAsync(db, req.Uuid);

            var vaultBalance = vault?.Balance ?? 0m;
            var bankBalance = (await db.UserBanks.FirstOrDefaultAsync(x => x.Uuid == req.Uuid))?.Balance ?? 0m;

            decimal vaultDelta;
            decimal bankDelta;
            if (req.Direction == VaultMoveDirection.VaultToBank)
            {
                // /deposit: 電子マネーを減らして銀行へ。
                if (vaultBalance < req.Amount)
                    return ApiResult<VaultMoveData>.Fail(ErrorCode.InsufficientFunds);
                vaultDelta = -req.Amount;
                bankDelta = req.Amount;
            }
            else
            {
                // /withdraw: 銀行を減らして電子マネーへ。電子マネーの更新後残高が上限を超える場合は拒否。
                if (bankBalance < req.Amount)
                    return ApiResult<VaultMoveData>.Fail(ErrorCode.InsufficientFunds);
                if (req.Amount > _options.MaxBalance || vaultBalance + req.Amount > _options.MaxBalance)
                    return ApiResult<VaultMoveData>.Fail(ErrorCode.BalanceLimitExceeded);
                vaultDelta = req.Amount;
                bankDelta = -req.Amount;
            }

            // vault 側更新(version++ / vault_log)
            var vaultData = await VaultRepository.ChangeBalanceCoreAsync(
                db, req.Uuid, player, vaultDelta,
                req.PluginName, req.Note, req.DisplayNote, req.Server, VaultSource.MAN10_API, req.OperationId);

            // bank 側更新(既存 Bank 経路: user_bank + money_log を同一 Tx)
            var newBankBalance = await BankRepository.ChangeBalanceCoreAsync(
                db, req.Uuid, player, bankDelta,
                req.PluginName, req.Note, req.DisplayNote, req.Server);

            return ApiResult<VaultMoveData>.Ok(new VaultMoveData(vaultData.Balance, vaultData.Version, newBankBalance));
        });

        if (res.IsSuccess && res.Data != null)
            await SafePushAsync(new VaultBalanceChange(req.Uuid, res.Data.VaultBalance, res.Data.VaultVersion, "MOVE", req.OperationId, req.Server));

        return res;
    }

    // ===================== 内部ヘルパ =====================

    // 単一 UUID の vault 書き込みを RunExclusiveAsync で実行し、成功時に push する共通処理。
    private async Task<ApiResult<VaultBalanceData>> RunVaultWriteAsync(
        string uuid, string cause, string? operationId, string originServer,
        Func<BankDbContext, Task<ApiResult<VaultBalanceData>>> work)
    {
        var res = await _bankService.RunExclusiveAsync(work);
        if (res.IsSuccess && res.Data != null)
            await SafePushAsync(new VaultBalanceChange(uuid, res.Data.Balance, res.Data.Version, cause, operationId, originServer));
        return res;
    }

    // 冪等再送の判定。operation_id が既に存在すれば現在の権威残高を返す(再適用しない)。
    private async Task<ApiResult<VaultBalanceData>?> TryReplayAsync(BankDbContext db, string uuid, string? operationId)
    {
        var existing = await VaultRepository.FindLogByOperationIdAsync(db, operationId);
        if (existing == null) return null;
        var data = await _vaultRepo.GetBalanceAsync(uuid);
        return ApiResult<VaultBalanceData>.Ok(data);
    }

    // プレイヤー名解決。既存の user_vault / user_bank 行から取得し、無ければプロフィールサービスへ。
    // 直列化ワーカー内での外部 HTTP 呼び出しを既存プレイヤーでは回避する。
    private async Task<string?> ResolvePlayerAsync(BankDbContext db, string uuid)
    {
        var v = await db.UserVaults.AsNoTracking().FirstOrDefaultAsync(x => x.Uuid == uuid);
        if (v != null && !string.IsNullOrWhiteSpace(v.Player)) return v.Player;
        var b = await db.UserBanks.AsNoTracking().FirstOrDefaultAsync(x => x.Uuid == uuid);
        if (b != null && !string.IsNullOrWhiteSpace(b.Player)) return b.Player;
        return await _profileService.GetNameByUuidAsync(uuid);
    }

    // push 失敗はコミット済み操作を失敗にしない。例外は握りつぶしてログのみ残す。
    private async Task SafePushAsync(VaultBalanceChange change)
    {
        try
        {
            await _notifier.PushBalanceAsync(change);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vault push に失敗しました(uuid={Uuid}, cause={Cause})。定期再同期で収束します。", change.Uuid, change.Cause);
        }
    }
}
