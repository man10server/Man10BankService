using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Database;

public class UserBank
{
    public int Id { get; set; }
    [StringLength(16)]
    public required string Player { get; set; }
    [StringLength(36)]
    public required string Uuid { get; set; }

    // 残高のマイナス突き抜け防止は、行ロック下のサービス/リポジトリ層の事前チェックで担保する。
    // セッターでの負値例外は廃止(EF の実体化でも発火しうるため。DESIGN 2.2)。
    public decimal Balance { get; set; }
}
