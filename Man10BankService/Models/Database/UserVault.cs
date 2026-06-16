using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Database;

// 電子マネー残高の真実(source of truth)テーブル。
// user_bank(銀行残高)とは別管理し、Uuid を UNIQUE にして1プレイヤー1行とする。
// Version は変更毎に単調増加させ、各 Paper サーバーへの push 適用順序の基準にする(VaultProvider 6.1)。
public class UserVault
{
    public int Id { get; set; }
    [StringLength(16)]
    public required string Player { get; set; }
    [StringLength(36)]
    public required string Uuid { get; set; }

    // 残高のマイナス突き抜け防止は、行ロック下のサービス/リポジトリ層の事前チェックで担保する
    // (UserBank と同方針。セッターでの負値例外は設けない)。
    public decimal Balance { get; set; }

    // 変更毎に ++。push イベントの順序判定(event.version > cache.version)に使う単調増加カウンタ。
    public long Version { get; set; }
}
