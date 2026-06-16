namespace Man10BankService.Models.Responses;

// 資産スナップショット更新レスポンス。JSONは { "updated": true|false }。
// EstateNoChange のとき updated=false となり、ErrorCode の闇落ちをなくす。
public sealed record SnapshotResponse(bool Updated);
