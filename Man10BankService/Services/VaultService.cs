using Man10BankService.Data;
using Man10BankService.Models.Requests;
using Man10BankService.Models.Responses;
using Man10BankService.Repositories;
using Man10BankService.Validation;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

// 電子マネー(user_vault)のサービス層。
// - 書き込みは銀行と同じ直列化キュー(BankService.RunExclusiveAsync)を共用し、
//   電子マネー⇄銀行を跨ぐ move を 1 トランザクションで原子化する(VaultProvider 7.1)。
// - すべての書き込みは対象 UUID の session claim(在席サーバー+sessionId)一致を要求する
//   (単一書き込み者。VaultProvider 8.2)。残高 push は行わない。
// - deposit/withdraw は operationId による冪等リプレイに対応する(Provider 送信キュー用)。
// - 残高上限 Vault:MaxBalance(既定1兆)を権威として検証し、claim 応答で Paper へ配布する。
public class VaultService
{
    private const decimal DefaultMaxBalance = 1_000_000_000_000m;

    private readonly IDbContextFactory<BankDbContext> _dbFactory;
    private readonly BankService _bankService;
    private readonly IPlayerProfileService _profileService;
    private readonly VaultSessionManager _sessions;
    private readonly IVaultSessionChannel _channel;
    private readonly ILogger<VaultService>? _logger;

    private readonly decimal _maxBalance;
    private readonly bool _configValid;

    public VaultService(
        IDbContextFactory<BankDbContext> dbFactory,
        BankService bankService,
        IPlayerProfileService profileService,
        VaultSessionManager sessions,
        IVaultSessionChannel channel,
        IConfiguration configuration,
        ILogger<VaultService>? logger = null)
    {
        _dbFactory = dbFactory;
        _bankService = bankService;
        _profileService = profileService;
        _sessions = sessions;
        _channel = channel;
        _logger = logger;

        // 残高上限は設定値を権威とする。不正な設定では Vault API 全体を fail-closed で停止する(VaultProvider 12)。
        _maxBalance = configuration.GetValue("Vault:MaxBalance", DefaultMaxBalance);
        _configValid = _maxBalance >= 1m && _maxBalance <= VaultAmountLimits.MaxSafe;
        if (!_configValid)
        {
            _logger?.LogCritical(
                "Vault:MaxBalance が不正なため電子マネーAPIを停止します(1 以上 {Max} 以下が必要): {Value}",
                VaultAmountLimits.MaxSafe, _maxBalance);
        }
    }

    // 書き込み1件分の確定結果(冪等リプレイでも同じ形で返す)。
    private sealed record Mutation(string Uuid, string Player, decimal Balance, long Version);

    // === 読み取り ===

    public async Task<ApiResult<VaultBalanceResponse>> GetBalanceAsync(string uuid)
    {
        if (!_configValid)
            return ApiResult<VaultBalanceResponse>.Fail(ErrorCode.VaultConfigInvalid);
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

    public async Task<ApiResult<List<Models.Database.VaultLog>>> GetLogsAsync(string uuid, int limit = 100, int offset = 0)
    {
        if (!_configValid)
            return ApiResult<List<Models.Database.VaultLog>>.Fail(ErrorCode.VaultConfigInvalid);
        if (limit is < 1 or > 1000)
            return ApiResult<List<Models.Database.VaultLog>>.Fail(ErrorCode.LimitOutOfRange);
        if (offset < 0)
            return ApiResult<List<Models.Database.VaultLog>>.Fail(ErrorCode.OffsetOutOfRange);
        try
        {
            var repo = new VaultRepository(_dbFactory);
            var logs = await repo.GetLogsAsync(uuid, limit, offset);
            return ApiResult<List<Models.Database.VaultLog>>.Ok(logs);
        }
        catch (Exception)
        {
            return ApiResult<List<Models.Database.VaultLog>>.Fail(ErrorCode.UnexpectedError);
        }
    }

    // === session claim / release ===

    // join 時の session claim(後勝ち)。口座を冪等作成し、確定残高+version+残高上限を配布する。
    // claim は同期チャネル(WS)接続中のサーバーからのみ受け付け、session の寿命をその接続に紐づける。
    public async Task<ApiResult<VaultSessionClaimResponse>> ClaimSessionAsync(VaultSessionClaimRequest req)
    {
        if (!_configValid)
            return ApiResult<VaultSessionClaimResponse>.Fail(ErrorCode.VaultConfigInvalid);

        var connectionId = _channel.GetConnectionId(req.Server);
        if (connectionId == null)
            return ApiResult<VaultSessionClaimResponse>.Fail(ErrorCode.ServerNotConnected);

        var res = await _bankService.RunExclusiveAsync(async db =>
        {
            var vault = await VaultRepository.GetOrCreateForUpdateAsync(db, req.Uuid, req.Player);
            return ApiResult<Mutation>.Ok(new Mutation(vault.Uuid, vault.Player, vault.Balance, vault.Version));
        });
        if (!res.IsSuccess || res.Data == null)
            return ApiResult<VaultSessionClaimResponse>.Fail(res.Code);

        var (claimed, revoked) = _sessions.Claim(req.Uuid, req.Player, req.Server, connectionId);

        // 旧 session が別サーバーのものなら失効を通知する(ベストエフォート)。
        if (revoked != null && !string.Equals(revoked.Server, req.Server, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await _channel.NotifySessionRevokedAsync(revoked.Server, req.Uuid, revoked.SessionId, req.Server);
            }
            catch (Exception e)
            {
                _logger?.LogWarning(e, "session 失効通知に失敗しました server={Server} uuid={Uuid}", revoked.Server, req.Uuid);
            }
        }

        return ApiResult<VaultSessionClaimResponse>.Ok(
            new VaultSessionClaimResponse(claimed.SessionId, res.Data.Balance, res.Data.Version, _maxBalance));
    }

    // quit 時の release。sessionId 不一致(後勝ちで置き換え済み等)でも成功扱いにする(冪等)。
    public Task<ApiResult<bool>> ReleaseSessionAsync(VaultSessionReleaseRequest req)
    {
        if (!_configValid)
            return Task.FromResult(ApiResult<bool>.Fail(ErrorCode.VaultConfigInvalid));
        var removed = _sessions.Release(req.Uuid, req.SessionId);
        return Task.FromResult(ApiResult<bool>.Ok(removed));
    }

    public ApiResult<VaultSessionStatusResponse> GetSession(string uuid)
    {
        if (!_configValid)
            return ApiResult<VaultSessionStatusResponse>.Fail(ErrorCode.VaultConfigInvalid);
        var s = _sessions.Get(uuid);
        return ApiResult<VaultSessionStatusResponse>.Ok(
            new VaultSessionStatusResponse(s != null, s?.Server, s?.SessionId));
    }

    // === 口座 ===

    // 口座を冪等に作成する(hasAccount/createPlayerAccount 用)。残高変更を伴わないため session は要求しない。
    public async Task<ApiResult<VaultBalanceResponse>> EnsureAccountAsync(string uuid)
    {
        if (!_configValid)
            return ApiResult<VaultBalanceResponse>.Fail(ErrorCode.VaultConfigInvalid);

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

    // === 書き込み ===

    public async Task<ApiResult<VaultBalanceResponse>> DepositAsync(VaultDepositRequest req)
    {
        var guard = GuardWrite<VaultBalanceResponse>(req.Uuid, req.Server, req.SessionId, req.Amount);
        if (guard != null)
            return guard;

        var source = req.Source ?? (req.OperationId != null ? VaultSources.Provider : VaultSources.Man10Api);
        var res = await _bankService.RunExclusiveAsync(async db =>
        {
            // 冪等リプレイ: 同一 operationId の再送には保存済みの結果を返す(VaultProvider 8.2)。
            var replay = await TryReplayAsync(db, req.OperationId);
            if (replay != null)
                return ApiResult<Mutation>.Ok(replay);

            var player = await _profileService.GetNameByUuidAsync(req.Uuid);
            if (player == null)
                return ApiResult<Mutation>.Fail(ErrorCode.PlayerNotFound);

            // 行ロック下で更新後残高の上限を検証する(VaultProvider 12)。
            var existing = await DbLockHelper.GetUserVaultForUpdateAsync(db, req.Uuid);
            if ((existing?.Balance ?? 0m) + req.Amount > _maxBalance)
                return ApiResult<Mutation>.Fail(ErrorCode.MaxBalanceExceeded);

            var vault = await VaultRepository.ChangeBalanceCoreAsync(
                db, req.Uuid, player, req.Amount, req.PluginName, req.Note, req.DisplayNote, req.Server,
                source, req.SessionId, req.OperationId);
            return ApiResult<Mutation>.Ok(new Mutation(vault.Uuid, vault.Player, vault.Balance, vault.Version));
        });

        return ToBalanceResponse(res);
    }

    public async Task<ApiResult<VaultBalanceResponse>> WithdrawAsync(VaultWithdrawRequest req)
    {
        var guard = GuardWrite<VaultBalanceResponse>(req.Uuid, req.Server, req.SessionId, req.Amount);
        if (guard != null)
            return guard;

        var source = req.Source ?? (req.OperationId != null ? VaultSources.Provider : VaultSources.Man10Api);
        var res = await _bankService.RunExclusiveAsync(async db =>
        {
            var replay = await TryReplayAsync(db, req.OperationId);
            if (replay != null)
                return ApiResult<Mutation>.Ok(replay);

            var player = await _profileService.GetNameByUuidAsync(req.Uuid);
            if (player == null)
                return ApiResult<Mutation>.Fail(ErrorCode.PlayerNotFound);

            // 行ロック下で残高不足を判定してから減算する(作成は ChangeBalanceCoreAsync に一本化する)。
            var existing = await DbLockHelper.GetUserVaultForUpdateAsync(db, req.Uuid);
            if ((existing?.Balance ?? 0m) < req.Amount)
                return ApiResult<Mutation>.Fail(ErrorCode.InsufficientFunds);

            var updated = await VaultRepository.ChangeBalanceCoreAsync(
                db, req.Uuid, player, -req.Amount, req.PluginName, req.Note, req.DisplayNote, req.Server,
                source, req.SessionId, req.OperationId);
            return ApiResult<Mutation>.Ok(new Mutation(updated.Uuid, updated.Player, updated.Balance, updated.Version));
        });

        return ToBalanceResponse(res);
    }

    // 電子マネー → 電子マネー送金(/pay)。送金元・送金先の両 claim が要求元サーバーと一致する場合のみ、
    // 両者を 1 Tx で更新して両残高+version を返す(VaultProvider 8.1)。
    public async Task<ApiResult<VaultTransferResponse>> TransferAsync(VaultTransferRequest req)
    {
        if (!_configValid)
            return ApiResult<VaultTransferResponse>.Fail(ErrorCode.VaultConfigInvalid);
        if (!_sessions.Validate(req.FromUuid, req.Server, req.FromSessionId) ||
            !_sessions.Validate(req.ToUuid, req.Server, req.ToSessionId))
            return ApiResult<VaultTransferResponse>.Fail(ErrorCode.SessionMismatch);
        if (req.Amount > _maxBalance)
            return ApiResult<VaultTransferResponse>.Fail(ErrorCode.MaxBalanceExceeded);

        return await _bankService.RunExclusiveAsync(async db =>
        {
            if (string.Equals(req.FromUuid, req.ToUuid, StringComparison.OrdinalIgnoreCase))
                return ApiResult<VaultTransferResponse>.Fail(ErrorCode.ValidationError);

            var fromPlayer = await _profileService.GetNameByUuidAsync(req.FromUuid);
            if (fromPlayer == null)
                return ApiResult<VaultTransferResponse>.Fail(ErrorCode.PlayerNotFound);
            var toPlayer = await _profileService.GetNameByUuidAsync(req.ToUuid);
            if (toPlayer == null)
                return ApiResult<VaultTransferResponse>.Fail(ErrorCode.PlayerNotFound);

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

            // 送金元の残高不足と送金先の上限超過を、ロック取得後の最新値で判定する。
            var fromVault = await DbLockHelper.GetUserVaultForUpdateAsync(db, req.FromUuid);
            if ((fromVault?.Balance ?? 0m) < req.Amount)
                return ApiResult<VaultTransferResponse>.Fail(ErrorCode.InsufficientFunds);
            var toVault = await DbLockHelper.GetUserVaultForUpdateAsync(db, req.ToUuid);
            if ((toVault?.Balance ?? 0m) + req.Amount > _maxBalance)
                return ApiResult<VaultTransferResponse>.Fail(ErrorCode.MaxBalanceExceeded);

            var newFrom = await VaultRepository.ChangeBalanceCoreAsync(
                db, req.FromUuid, fromPlayer, -req.Amount, req.PluginName, req.Note, req.DisplayNote, req.Server,
                VaultSources.Man10Api, req.FromSessionId, null);
            var newTo = await VaultRepository.ChangeBalanceCoreAsync(
                db, req.ToUuid, toPlayer, req.Amount, req.PluginName, req.Note, req.DisplayNote, req.Server,
                VaultSources.Man10Api, req.ToSessionId, null);

            return ApiResult<VaultTransferResponse>.Ok(new VaultTransferResponse(
                newFrom.Balance, newFrom.Version, newTo.Balance, newTo.Version));
        });
    }

    // 管理用: 絶対値設定。他の書き込みと同様、在席サーバー(claim 一致)からのみ実行できる(VaultProvider 5.4)。
    public async Task<ApiResult<VaultBalanceResponse>> SetAsync(VaultSetRequest req)
    {
        var guard = GuardWrite<VaultBalanceResponse>(req.Uuid, req.Server, req.SessionId, req.Amount);
        if (guard != null)
            return guard;

        var res = await _bankService.RunExclusiveAsync(async db =>
        {
            var player = await _profileService.GetNameByUuidAsync(req.Uuid);
            if (player == null)
                return ApiResult<Mutation>.Fail(ErrorCode.PlayerNotFound);

            var (vault, _) = await VaultRepository.SetBalanceCoreAsync(
                db, req.Uuid, player, req.Amount, req.PluginName, req.Note, req.DisplayNote, req.Server,
                VaultSources.Admin, req.SessionId);
            return ApiResult<Mutation>.Ok(new Mutation(vault.Uuid, vault.Player, vault.Balance, vault.Version));
        });

        return ToBalanceResponse(res);
    }

    // 電子マネー ⇄ 銀行残高を 1 Tx で移動する(VaultProvider 11.2)。
    // ロックは固定順(user_vault → user_bank)で取得する。
    public async Task<ApiResult<VaultMoveResponse>> MoveAsync(VaultMoveRequest req)
    {
        var guard = GuardWrite<VaultMoveResponse>(req.Uuid, req.Server, req.SessionId, req.Amount);
        if (guard != null)
            return guard;

        return await _bankService.RunExclusiveAsync(async db =>
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
                // 電子マネー側が増える方向は残高上限を検証する(VaultProvider 12)。
                if (vaultBalance + req.Amount > _maxBalance)
                    return ApiResult<VaultMoveResponse>.Fail(ErrorCode.MaxBalanceExceeded);
                vaultDelta = req.Amount;
                bankDelta = -req.Amount;
            }

            var newVault = await VaultRepository.ChangeBalanceCoreAsync(
                db, req.Uuid, player, vaultDelta, req.PluginName, req.Note, req.DisplayNote, req.Server,
                VaultSources.Man10Api, req.SessionId, null);
            var newBankBalance = await BankRepository.ChangeBalanceCoreAsync(
                db, req.Uuid, player, bankDelta, req.PluginName, req.Note, req.DisplayNote, req.Server);

            return ApiResult<VaultMoveResponse>.Ok(
                new VaultMoveResponse(newVault.Balance, newBankBalance, newVault.Version));
        });
    }

    // === 内部 ===

    // 書き込み共通の前提検査: 設定妥当・session 一致・操作金額の上限(VaultProvider 8.2/12)。
    private ApiResult<T>? GuardWrite<T>(string uuid, string server, string sessionId, decimal amount)
    {
        if (!_configValid)
            return ApiResult<T>.Fail(ErrorCode.VaultConfigInvalid);
        if (!_sessions.Validate(uuid, server, sessionId))
            return ApiResult<T>.Fail(ErrorCode.SessionMismatch);
        if (amount > _maxBalance)
            return ApiResult<T>.Fail(ErrorCode.MaxBalanceExceeded);
        return null;
    }

    // 同一 operationId の既存ログがあれば、その時点の確定結果を返す(なければ null)。
    // 書き込みは BankService の直列化キュー内で行われるため、単一インスタンスではこの
    // 事前チェックで二重適用を防げる。多重インスタンス時は UNIQUE 制約が最終防壁になる
    // (制約違反は UnexpectedError となり、プラグイン側の再送で本リプレイに合流する)。
    private static async Task<Mutation?> TryReplayAsync(BankDbContext db, string? operationId)
    {
        if (string.IsNullOrEmpty(operationId))
            return null;
        var existing = await VaultRepository.FindByOperationIdAsync(db, operationId);
        return existing == null
            ? null
            : new Mutation(existing.Uuid, existing.Player, existing.BalanceAfter, existing.VersionAfter);
    }

    private static ApiResult<VaultBalanceResponse> ToBalanceResponse(ApiResult<Mutation> res)
    {
        return res.IsSuccess && res.Data != null
            ? ApiResult<VaultBalanceResponse>.Ok(new VaultBalanceResponse(res.Data.Balance, res.Data.Version))
            : ApiResult<VaultBalanceResponse>.Fail(res.Code);
    }
}

// vault_log.source の定数(VaultProvider 9.2)。
public static class VaultSources
{
    public const string Provider = "PROVIDER";
    public const string Man10Api = "MAN10_API";
    public const string Admin = "ADMIN";
}
