namespace Man10BankService.Models.Responses;

// あるプレイヤーが使っているサーバーローンの契約と、その共有(リンク)の状態。
//  - OwnerUuid / OwnerPlayer: 契約の本垢。誰の契約にも参加していなければ本人。
//  - Members: この契約に参加しているアカウント(参加が古い順)。
//  - PendingRequests: この契約への参加申請のうち、本垢の承認を待っているもの。
//  - OutgoingRequest: 本人が他のプレイヤーの契約へ出している、承認待ちの申請。
public sealed record ServerLoanContractResponse(
    string OwnerUuid,
    string OwnerPlayer,
    List<ServerLoanMemberResponse> Members,
    List<ServerLoanMemberResponse> PendingRequests,
    ServerLoanLinkRequestResponse? OutgoingRequest);

public sealed record ServerLoanMemberResponse(
    string Uuid,
    string Player,
    string LinkedBy,
    DateTime RequestedAt,
    DateTime? ApprovedAt);

public sealed record ServerLoanLinkRequestResponse(
    string OwnerUuid,
    string OwnerPlayer,
    DateTime RequestedAt);
