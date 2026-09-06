using System.ComponentModel.DataAnnotations;
using Man10BankService.Validation;

namespace Man10BankService.Models.Requests;

// Paper サーバーが join 時にプレイヤーの vault session を claim する(VaultProvider 5.8)。
// 後勝ち(last-write-wins)で、旧 session は失効し旧サーバーへ WS で通知される。
// 応答で確定残高・version・残高上限(権威設定値)を配布する。
public class VaultSessionClaimRequest
{
    [Required]
    [StringLength(36)]
    [RegularExpression(UuidValidation.Pattern, ErrorMessage = "UUID の形式が不正です。")]
    public required string Uuid { get; set; }

    // プレイヤー名(口座作成・名前更新に使う)。
    [Required]
    [StringLength(16)]
    public required string Player { get; set; }

    [Required]
    [StringLength(16)]
    public required string Server { get; set; }
}
