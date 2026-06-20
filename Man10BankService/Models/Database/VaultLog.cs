using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Database;

// 電子マネー専用ログ。銀行の money_log とは分離する。
// operation_id は冪等キー(指定時 UNIQUE)。source / balance_after は監査・冪等応答用。
public class VaultLog
{
    public int Id { get; set; }

    [StringLength(16)]
    public required string Player { get; set; }

    [StringLength(36)]
    public required string Uuid { get; set; }

    [StringLength(32)]
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

    // Provider 送信待ちキュー / Man10BankAPI の冪等キー。null 可。指定時は UNIQUE。
    [StringLength(64)]
    public string? OperationId { get; set; }

    // 操作元種別(PROVIDER / MAN10_API / ADMIN / SYSTEM)。
    public VaultSource Source { get; set; }

    // 操作後残高。監査と冪等応答(再照会)用。
    public decimal BalanceAfter { get; set; }
}
