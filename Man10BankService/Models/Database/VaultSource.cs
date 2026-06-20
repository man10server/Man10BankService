namespace Man10BankService.Models.Database;

// vault_log の操作元種別。DB には文字列で保存する(BankDbContext で HasConversion<string>)。
public enum VaultSource
{
    // 外部 Vault Provider 経路(外部ショップなど)からの操作。
    PROVIDER,

    // 内製 API 経路(Man10BankAPI / VaultService)からの操作。
    MAN10_API,

    // 管理者操作(setBalance / editvault)。
    ADMIN,

    // システム内部操作(再同期・補正など)。
    SYSTEM
}
