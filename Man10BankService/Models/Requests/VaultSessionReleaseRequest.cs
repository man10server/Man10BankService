using System.ComponentModel.DataAnnotations;
using Man10BankService.Validation;

namespace Man10BankService.Models.Requests;

// quit 時の session release。sessionId が現在の claim と一致する場合のみ解除する
// (古い release が後勝ちの新 session を壊さないため)。不一致・解除済みでも 204 を返す。
public class VaultSessionReleaseRequest
{
    [Required]
    [StringLength(36)]
    [RegularExpression(UuidValidation.Pattern, ErrorMessage = "UUID の形式が不正です。")]
    public required string Uuid { get; set; }

    [Required]
    [StringLength(64)]
    public required string SessionId { get; set; }

    [Required]
    [StringLength(16)]
    public required string Server { get; set; }
}
