using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Requests;

// ログイン時のプレイヤー名簿登録。UUID と現在の Minecraft ID を送る(既存なら名前を更新)。
public class PlayerRegisterRequest
{
    [Required]
    [StringLength(36)]
    [RegularExpression(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", ErrorMessage = "UUID の形式が不正です。")]
    public required string Uuid { get; set; }

    [Required]
    [StringLength(16, MinimumLength = 1)]
    public required string Name { get; set; }
}
