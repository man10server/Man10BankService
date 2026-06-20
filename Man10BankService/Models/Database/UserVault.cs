using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Database;

// 電子マネー残高(user_vault)。user_bank とは分離した唯一の真実。
// Uuid は UNIQUE。Version は楽観制御・古い push 破棄のための単調増加カウンタ。
public class UserVault
{
    public int Id { get; set; }

    [StringLength(16)]
    public required string Player { get; set; }

    [StringLength(36)]
    public required string Uuid { get; set; }

    // 残高のマイナス突き抜け防止は、行ロック下のサービス/リポジトリ層の事前チェックで担保する。
    public decimal Balance { get; set; }

    // 残高更新ごとに +1 する版数。古い push / 再同期結果を捨てるために使う。
    public long Version { get; set; }
}
