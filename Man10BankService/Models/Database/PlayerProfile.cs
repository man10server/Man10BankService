using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Database;

// ログイン時にプラグインから登録されるプレイヤー名簿。UUID と現在の Minecraft ID を保持し、
// 改名はログインのたびに追従する。名前解決はこの名簿を最優先に使い、外部 API(Mojang)には依存しない。
public class PlayerProfile
{
    [StringLength(36)]
    public required string Uuid { get; set; }
    [StringLength(16)]
    public required string Name { get; set; }
    public DateTime UpdatedAt { get; set; }
}
