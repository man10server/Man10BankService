using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Database;

// 電子マネーの取引ログ。銀行の money_log とは混ぜず専用テーブル vault_log に分離する(VaultProvider 6.2)。
// 形は MoneyLog を踏襲する。
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
}
