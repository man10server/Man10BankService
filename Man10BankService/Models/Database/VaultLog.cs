using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Database;

// 電子マネーの取引ログ。銀行の money_log とは混ぜず専用テーブル vault_log に分離する(VaultProvider 9.2)。
// 形は MoneyLog を踏襲しつつ、冪等キー・操作元・session・操作後残高を監査用に持つ。
public class VaultLog
{
    public int Id { get; set; }
    [StringLength(16)]
    public required string Player { get; set; }
    [StringLength(36)]
    public required string Uuid { get; set; }
    [StringLength(16)]
    public required string PluginName { get; set; }
    public decimal Amount { get; set; }
    [StringLength(64)]
    public required string Note { get; set; }
    [StringLength(64)]
    public required string DisplayNote { get; set; }
    [StringLength(16)]
    public required string Server { get; set; }
    public bool Deposit { get; set; }
    public DateTime Date { get; set; }

    // 冪等キー(Provider 送信キューの deposit/withdraw のみ指定)。指定時は UNIQUE で、
    // 同一キーの再送には保存済みの BalanceAfter/VersionAfter を同じ結果として返す(VaultProvider 6.4)。
    [StringLength(36)]
    public string? OperationId { get; set; }

    // 操作元種別: PROVIDER / MAN10_API / ADMIN / SYSTEM(VaultProvider 9.2)。
    [StringLength(16)]
    public string Source { get; set; } = "";

    // 操作時の session claim 識別子(監査用)。
    [StringLength(64)]
    public string? SessionId { get; set; }

    // 操作後残高と version(監査・冪等応答用)。
    public decimal BalanceAfter { get; set; }
    public long VersionAfter { get; set; }
}
