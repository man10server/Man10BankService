using Man10BankService.Data;
using Man10BankService.Models.Database;
using Man10BankService.Models.Responses;
using Man10BankService.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

// 契約の共有(リンク): 他のプレイヤーの契約への参加・承認・解除。
// 参加したアカウントの残債は本垢の契約へ移り、以後は本垢の契約を共有する
// (どの契約を使うかの読み替えは ServerLoanRepository.ResolveContractUuidAsync)。
//  - 1 アカウントが参加できる契約は 1 つ。参加者がいる本垢は、他の契約に参加できない(数珠つなぎにしない)。
//  - 参加者を紐付け先に指定された場合は、その本垢の契約につなぐ。
public partial class ServerLoanService
{
    private const int LinkedByMaxLength = 16;

    // uuid が使っている契約と、共有の状態を返す。
    public async Task<ApiResult<ServerLoanContractResponse>> GetContractAsync(string uuid)
    {
        try
        {
            if (await _profileService.GetNameByUuidAsync(uuid) == null)
                return ApiResult<ServerLoanContractResponse>.Fail(ErrorCode.PlayerNotFound);

            await using var db = await _dbFactory.CreateDbContextAsync();
            var ownerUuid = await ServerLoanRepository.ResolveContractUuidAsync(db, uuid);
            var rows = await db.ServerLoanMembers
                .AsNoTracking()
                .Where(x => x.OwnerUuid == ownerUuid || x.Uuid == uuid)
                .ToListAsync();

            var members = new List<ServerLoanMemberResponse>();
            foreach (var row in rows.Where(x => x.OwnerUuid == ownerUuid && x.ApprovedAt != null).OrderBy(x => x.ApprovedAt).ThenBy(x => x.Uuid))
                members.Add(await ToMemberResponseAsync(row));

            var pending = new List<ServerLoanMemberResponse>();
            foreach (var row in rows.Where(x => x.OwnerUuid == ownerUuid && x.ApprovedAt == null).OrderBy(x => x.RequestedAt).ThenBy(x => x.Uuid))
                pending.Add(await ToMemberResponseAsync(row));

            var outgoingRow = rows.FirstOrDefault(x => x.Uuid == uuid && x.ApprovedAt == null);
            var outgoing = outgoingRow == null
                ? null
                : new ServerLoanLinkRequestResponse(outgoingRow.OwnerUuid, await GetPlayerNameAsync(outgoingRow.OwnerUuid), outgoingRow.RequestedAt);

            return ApiResult<ServerLoanContractResponse>.Ok(
                new ServerLoanContractResponse(ownerUuid, await GetPlayerNameAsync(ownerUuid), members, pending, outgoing));
        }
        catch (Exception)
        {
            return ApiResult<ServerLoanContractResponse>.Fail(ErrorCode.UnexpectedError);
        }
    }

    // 参加の申請: uuid が targetUuid の契約への参加を申請する。本垢が承認するまで契約は共有されない。
    public async Task<ApiResult<ServerLoanContractResponse>> RequestLinkAsync(string uuid, string targetUuid)
    {
        var applicant = await _bank.ResolvePlayerNameAsync(uuid);
        if (!applicant.IsSuccess)
            return ApiResult<ServerLoanContractResponse>.Fail(applicant.Code);
        if (await _profileService.GetNameByUuidAsync(targetUuid) == null)
            return ApiResult<ServerLoanContractResponse>.Fail(ErrorCode.PlayerNotFound);

        var result = await _bank.RunExclusiveAsync<bool>(async db =>
        {
            var ownerUuid = await ServerLoanRepository.ResolveContractUuidAsync(db, targetUuid);
            if (ownerUuid == uuid)
                return ApiResult<bool>.Fail(ErrorCode.LinkInvalidTarget);
            if (await db.ServerLoanMembers.AnyAsync(x => x.Uuid == uuid))
                return ApiResult<bool>.Fail(ErrorCode.LinkAlreadyLinked);
            if (await ServerLoanRepository.HasMembersAsync(db, uuid))
                return ApiResult<bool>.Fail(ErrorCode.LinkHasMembers);
            if (await IsEitherOverdueAsync(db, uuid, ownerUuid))
                return ApiResult<bool>.Fail(ErrorCode.LinkBlockedByOverdue);

            db.ServerLoanMembers.Add(new ServerLoanMember
            {
                Uuid = uuid,
                OwnerUuid = ownerUuid,
                LinkedBy = TrimLinkedBy(applicant.Data!),
            });
            return ApiResult<bool>.Ok(true);
        });

        return await ToContractResultAsync(result, uuid);
    }

    // 申請の承認: 本垢 ownerUuid が、memberUuid からの参加申請を承認する。
    public async Task<ApiResult<ServerLoanContractResponse>> ApproveLinkAsync(string ownerUuid, string memberUuid)
    {
        var moved = 0m;
        var result = await _bank.RunExclusiveAsync<bool>(async db =>
        {
            var row = await db.ServerLoanMembers
                .FirstOrDefaultAsync(x => x.Uuid == memberUuid && x.OwnerUuid == ownerUuid && x.ApprovedAt == null);
            if (row == null)
                return ApiResult<bool>.Fail(ErrorCode.LinkRequestNotFound);
            // 申請を受けた後に、本垢が他の契約に参加した場合は承認できない。
            if (await ServerLoanRepository.ResolveContractUuidAsync(db, ownerUuid) != ownerUuid)
                return ApiResult<bool>.Fail(ErrorCode.LinkAlreadyLinked);
            if (await ServerLoanRepository.HasMembersAsync(db, memberUuid))
                return ApiResult<bool>.Fail(ErrorCode.LinkHasMembers);
            if (await IsEitherOverdueAsync(db, memberUuid, ownerUuid))
                return ApiResult<bool>.Fail(ErrorCode.LinkBlockedByOverdue);

            row.ApprovedAt = DateTime.UtcNow;
            moved = await JoinContractCoreAsync(db, memberUuid, ownerUuid);
            return ApiResult<bool>.Ok(true);
        });

        if (result.IsSuccess)
            _logger?.LogInformation("サーバーローンの共有: {Member} が {Owner} の契約に参加しました(本垢が承認、移した残債 {Moved})。", memberUuid, ownerUuid, moved);
        return await ToContractResultAsync(result, ownerUuid);
    }

    // 申請の拒否: 本垢 ownerUuid が、memberUuid からの参加申請を取り下げさせる。
    public async Task<ApiResult<ServerLoanContractResponse>> RejectLinkAsync(string ownerUuid, string memberUuid)
    {
        var result = await RemovePendingRequestAsync(x => x.Uuid == memberUuid && x.OwnerUuid == ownerUuid);
        return await ToContractResultAsync(result, ownerUuid);
    }

    // 申請の取り消し: uuid が自分の出した参加申請を取り消す。
    public async Task<ApiResult<ServerLoanContractResponse>> CancelLinkRequestAsync(string uuid)
    {
        var result = await RemovePendingRequestAsync(x => x.Uuid == uuid);
        return await ToContractResultAsync(result, uuid);
    }

    // 脱退: uuid が参加している契約から抜ける。契約に残債がある間は抜けられない。
    public async Task<ApiResult<ServerLoanContractResponse>> LeaveLinkAsync(string uuid)
    {
        var ownerUuid = string.Empty;
        var result = await _bank.RunExclusiveAsync<bool>(async db =>
        {
            var row = await db.ServerLoanMembers.FirstOrDefaultAsync(x => x.Uuid == uuid && x.ApprovedAt != null);
            if (row == null)
                return ApiResult<bool>.Fail(ErrorCode.LinkNotMember);

            ownerUuid = row.OwnerUuid;
            var owner = await DbLockHelper.GetServerLoanForUpdateAsync(db, ownerUuid);
            if (owner is { BorrowAmount: > 0m })
                return ApiResult<bool>.Fail(ErrorCode.LinkDebtRemaining);

            await LeaveContractCoreAsync(db, row);
            return ApiResult<bool>.Ok(true);
        });

        if (result.IsSuccess)
            _logger?.LogInformation("サーバーローンの共有: {Member} が {Owner} の契約から脱退しました(本人の操作)。", uuid, ownerUuid);
        return await ToContractResultAsync(result, uuid);
    }

    // 運営による強制リンク: memberUuid を targetUuid の契約に、申請・承認なしで参加させる。延滞中でも行える。
    // 承認待ちの申請がある場合は置き換える。すでに他の契約に参加している場合は、先に解除する必要がある。
    // resetLimit を指定すると、罰則として参加先の契約の借入枠を初期枠(MinAmount)に戻す。
    public async Task<ApiResult<ServerLoanContractResponse>> ForceLinkAsync(
        string memberUuid, string targetUuid, string operatorName, bool resetLimit = false)
    {
        if (await _profileService.GetNameByUuidAsync(memberUuid) == null || await _profileService.GetNameByUuidAsync(targetUuid) == null)
            return ApiResult<ServerLoanContractResponse>.Fail(ErrorCode.PlayerNotFound);

        var ownerUuid = string.Empty;
        var moved = 0m;
        var result = await _bank.RunExclusiveAsync<bool>(async db =>
        {
            ownerUuid = await ServerLoanRepository.ResolveContractUuidAsync(db, targetUuid);
            if (ownerUuid == memberUuid)
                return ApiResult<bool>.Fail(ErrorCode.LinkInvalidTarget);
            if (await ServerLoanRepository.HasMembersAsync(db, memberUuid))
                return ApiResult<bool>.Fail(ErrorCode.LinkHasMembers);

            var row = await db.ServerLoanMembers.FirstOrDefaultAsync(x => x.Uuid == memberUuid);
            if (row is { ApprovedAt: not null })
                return ApiResult<bool>.Fail(ErrorCode.LinkAlreadyLinked);

            var now = DateTime.UtcNow;
            if (row == null)
            {
                row = new ServerLoanMember { Uuid = memberUuid, OwnerUuid = ownerUuid, LinkedBy = TrimLinkedBy(operatorName) };
                db.ServerLoanMembers.Add(row);
            }
            row.OwnerUuid = ownerUuid;
            row.LinkedBy = TrimLinkedBy(operatorName);
            row.RequestedAt = now;
            row.ApprovedAt = now;

            moved = await JoinContractCoreAsync(db, memberUuid, ownerUuid, resetLimit);
            return ApiResult<bool>.Ok(true);
        });

        if (result.IsSuccess)
            _logger?.LogInformation("サーバーローンの共有: {Member} を {Owner} の契約に参加させました(運営 {Operator} の操作、移した残債 {Moved}、枠の初期化 {ResetLimit})。", memberUuid, ownerUuid, operatorName, moved, resetLimit);
        return await ToContractResultAsync(result, memberUuid);
    }

    // 運営による解除: memberUuid の参加(または承認待ちの申請)を取り消す。契約に残債があっても行える。
    // 参加時に本垢の契約へ移した残債は戻さない。
    public async Task<ApiResult<ServerLoanContractResponse>> ForceUnlinkAsync(string memberUuid, string operatorName)
    {
        var ownerUuid = string.Empty;
        var result = await _bank.RunExclusiveAsync<bool>(async db =>
        {
            var row = await db.ServerLoanMembers.FirstOrDefaultAsync(x => x.Uuid == memberUuid);
            if (row == null)
                return ApiResult<bool>.Fail(ErrorCode.LinkNotMember);

            ownerUuid = row.OwnerUuid;
            if (row.ApprovedAt == null)
                db.ServerLoanMembers.Remove(row);
            else
                await LeaveContractCoreAsync(db, row);
            return ApiResult<bool>.Ok(true);
        });

        if (result.IsSuccess)
            _logger?.LogInformation("サーバーローンの共有: {Member} を {Owner} の契約から外しました(運営 {Operator} の操作)。", memberUuid, ownerUuid, operatorName);
        return await ToContractResultAsync(result, memberUuid);
    }

    // 参加の確定: memberUuid 自身の契約の残債を ownerUuid の契約へ移し、参加のログを残す。移した残債を返す。
    // server_loan_contract_member の行は呼び出し側が承認済みにしておくこと。
    // resetLimit を指定すると、参加先の契約の借入枠を初期枠に戻すログも残す。
    private async Task<decimal> JoinContractCoreAsync(BankDbContext db, string memberUuid, string ownerUuid, bool resetLimit = false)
    {
        var repo = new ServerLoanRepository(_dbFactory, _profileService);
        var owner = await repo.GetOrCreateForUpdateAsync(db, ownerUuid);
        var own = await DbLockHelper.GetServerLoanForUpdateAsync(db, memberUuid);

        var moved = Math.Max(0m, own?.BorrowAmount ?? 0m);
        if (own != null && moved > 0m)
            ServerLoanRepository.AdjustLoanCore(db, own, -moved, ServerLoanRepository.ServerLoanLogAction.SetBorrowAmount, actorUuid: memberUuid);

        // 支払額が未設定の契約に残債が移ってきた場合は、借入時と同じく初期値を設定する。
        var needPayment = owner.PaymentAmount <= 0m && moved > 0m;
        ServerLoanRepository.AdjustLoanCore(db, owner, moved, ServerLoanRepository.ServerLoanLogAction.MemberJoin, actorUuid: memberUuid);
        if (needPayment)
            owner.PaymentAmount = CalculateInitialPaymentAmount(owner.BorrowAmount, linked: true);
        if (resetLimit)
            ServerLoanRepository.AdjustLoanCore(db, owner, 0m, ServerLoanRepository.ServerLoanLogAction.LimitReset);

        return moved;
    }

    // 脱退の確定: server_loan_contract_member の行を消し、参加していた契約に脱退のログを残す。残債は本垢の契約に残す。
    private async Task LeaveContractCoreAsync(BankDbContext db, ServerLoanMember row)
    {
        var repo = new ServerLoanRepository(_dbFactory, _profileService);
        var owner = await repo.GetOrCreateForUpdateAsync(db, row.OwnerUuid);
        ServerLoanRepository.AdjustLoanCore(db, owner, 0m, ServerLoanRepository.ServerLoanLogAction.MemberLeave, actorUuid: row.Uuid);
        db.ServerLoanMembers.Remove(row);
    }

    private async Task<ApiResult<bool>> RemovePendingRequestAsync(System.Linq.Expressions.Expression<Func<ServerLoanMember, bool>> predicate)
    {
        return await _bank.RunExclusiveAsync<bool>(async db =>
        {
            var row = await db.ServerLoanMembers.Where(x => x.ApprovedAt == null).FirstOrDefaultAsync(predicate);
            if (row == null)
                return ApiResult<bool>.Fail(ErrorCode.LinkRequestNotFound);

            db.ServerLoanMembers.Remove(row);
            return ApiResult<bool>.Ok(true);
        });
    }

    private static async Task<bool> IsEitherOverdueAsync(BankDbContext db, string uuid, string otherUuid)
    {
        return await ServerLoanRepository.HasOverdueDebtAsync(db, uuid)
               || await ServerLoanRepository.HasOverdueDebtAsync(db, otherUuid);
    }

    private async Task<ApiResult<ServerLoanContractResponse>> ToContractResultAsync(ApiResult<bool> result, string uuid)
    {
        if (!result.IsSuccess)
            return ApiResult<ServerLoanContractResponse>.Fail(result.Code);
        return await GetContractAsync(uuid);
    }

    private async Task<ServerLoanMemberResponse> ToMemberResponseAsync(ServerLoanMember row)
    {
        return new ServerLoanMemberResponse(row.Uuid, await GetPlayerNameAsync(row.Uuid), row.LinkedBy, row.RequestedAt, row.ApprovedAt);
    }

    private async Task<string> GetPlayerNameAsync(string uuid)
    {
        return await _profileService.GetNameByUuidAsync(uuid) ?? string.Empty;
    }

    private static string TrimLinkedBy(string name)
    {
        return name.Length <= LinkedByMaxLength ? name : name[..LinkedByMaxLength];
    }
}
