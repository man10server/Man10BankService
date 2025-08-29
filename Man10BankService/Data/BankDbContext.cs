using Man10BankService.Models;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Data;

public class BankDbContext : DbContext
{
    public BankDbContext(DbContextOptions<BankDbContext> options) : base(options)
    {
    }

    public DbSet<AtmLog> AtmLogs => Set<AtmLog>();
    public DbSet<Cheque> Cheques => Set<Cheque>();
    public DbSet<Estate> Estates => Set<Estate>();
    public DbSet<EstateHistory> EstateHistories => Set<EstateHistory>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<MoneyLog> MoneyLogs => Set<MoneyLog>();
    public DbSet<ServerEstateHistory> ServerEstateHistories => Set<ServerEstateHistory>();
    public DbSet<ServerLoan> ServerLoans => Set<ServerLoan>();
    public DbSet<UserBank> UserBanks => Set<UserBank>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AtmLog>(e =>
        {
            e.ToTable("atm_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(20, 0);
        });

        modelBuilder.Entity<Cheque>(e =>
        {
            e.ToTable("cheque_tbl");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Used);
            e.Property(x => x.Amount).HasPrecision(20, 0);
            e.Property(x => x.UseDate).HasColumnName("use_date");
            e.Property(x => x.UsePlayer).HasColumnName("use_player");
        });

        modelBuilder.Entity<Estate>(e =>
        {
            e.ToTable("estate_tbl");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Uuid);
            e.Property(x => x.Vault).HasPrecision(20, 0);
            e.Property(x => x.Bank).HasPrecision(20, 0);
            e.Property(x => x.Cash).HasPrecision(20, 0);
            e.Property(x => x.EstateAmount).HasColumnName("estate").HasPrecision(20, 0);
            e.Property(x => x.Loan).HasPrecision(20, 0);
            e.Property(x => x.Shop).HasPrecision(20, 0);
            e.Property(x => x.Crypto).HasPrecision(20, 0);
            e.Property(x => x.Total).HasPrecision(20, 0);
        });

        modelBuilder.Entity<EstateHistory>(e =>
        {
            e.ToTable("estate_history_tbl");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Uuid);
            e.Property(x => x.Vault).HasPrecision(20, 0);
            e.Property(x => x.Bank).HasPrecision(20, 0);
            e.Property(x => x.Cash).HasPrecision(20, 0);
            e.Property(x => x.EstateAmount).HasColumnName("estate").HasPrecision(20, 0);
            e.Property(x => x.Loan).HasPrecision(20, 0);
            e.Property(x => x.Shop).HasPrecision(20, 0);
            e.Property(x => x.Crypto).HasPrecision(20, 0);
            e.Property(x => x.Total).HasPrecision(20, 0);
        });

        modelBuilder.Entity<Loan>(e =>
        {
            e.ToTable("loan_table");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BorrowPlayer, x.BorrowUuid });
            e.Property(x => x.Amount).HasPrecision(20, 0);
            e.Property(x => x.LendPlayer).HasColumnName("lend_player");
            e.Property(x => x.LendUuid).HasColumnName("lend_uuid");
            e.Property(x => x.BorrowPlayer).HasColumnName("borrow_player");
            e.Property(x => x.BorrowUuid).HasColumnName("borrow_uuid");
            e.Property(x => x.BorrowDate).HasColumnName("borrow_date");
            e.Property(x => x.PaybackDate).HasColumnName("payback_date");
            e.Property(x => x.CollateralItem).HasColumnName("collateral_item");
        });

        modelBuilder.Entity<MoneyLog>(e =>
        {
            e.ToTable("money_log");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Id, x.Uuid, x.Player });
            e.Property(x => x.Amount).HasPrecision(20, 0);
            e.Property(x => x.PluginName).HasColumnName("plugin_name");
            e.Property(x => x.DisplayNote).HasColumnName("display_note");
        });

        modelBuilder.Entity<ServerEstateHistory>(e =>
        {
            e.ToTable("server_estate_history");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Year, x.Month, x.Day, x.Hour });
            e.Property(x => x.Vault).HasPrecision(20, 0);
            e.Property(x => x.Bank).HasPrecision(20, 0);
            e.Property(x => x.Cash).HasPrecision(20, 0);
            e.Property(x => x.EstateAmount).HasColumnName("estate").HasPrecision(20, 0);
            e.Property(x => x.Loan).HasPrecision(20, 0);
            e.Property(x => x.Shop).HasPrecision(20, 0);
            e.Property(x => x.Crypto).HasPrecision(20, 0);
            e.Property(x => x.Total).HasPrecision(20, 0);
        });

        modelBuilder.Entity<ServerLoan>(e =>
        {
            e.ToTable("server_loan_tbl");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Uuid, x.BorrowAmount });
            e.Property(x => x.BorrowAmount).HasColumnName("borrow_amount").HasPrecision(20, 0);
            e.Property(x => x.PaymentAmount).HasColumnName("payment_amount").HasPrecision(20, 0);
            e.Property(x => x.BorrowDate).HasColumnName("borrow_date");
            e.Property(x => x.LastPayDate).HasColumnName("last_pay_date");
            e.Property(x => x.FailedPayment).HasColumnName("failed_payment");
            e.Property(x => x.StopInterest).HasColumnName("stop_interest");
            e.Property(x => x.Uuid).HasColumnName("uuid");
        });

        modelBuilder.Entity<UserBank>(e =>
        {
            e.ToTable("user_bank");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Id, x.Uuid, x.Player });
            e.Property(x => x.Balance).HasPrecision(20, 0);
        });
    }
}
