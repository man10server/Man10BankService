using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Database;

// 入出金・送金の冪等キー。プラグインが生成した requestId ごとに処理済みの結果(残高)を保持し、
// タイムアウト等で結果不明になった要求の再送時に、二重実行せず初回結果を返すために使う。
public class IdempotencyKey
{
    [StringLength(36)]
    public required string RequestId { get; set; }
    [StringLength(16)]
    public required string Operation { get; set; }
    [StringLength(36)]
    public required string Uuid { get; set; }
    public decimal Amount { get; set; }
    public decimal ResultBalance { get; set; }
    public DateTime CreatedAt { get; set; }
}
