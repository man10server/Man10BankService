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
        });

        modelBuilder.Entity<Estate>(e =>
        {
            e.ToTable("estate_tbl");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Uuid);
            e.Property(x => x.Vault).HasPrecision(20, 0);
            e.Property(x => x.Bank).HasPrecision(20, 0);
            e.Property(x => x.Cash).HasPrecision(20, 0);
            e.Property(x => x.EstateAmount).HasPrecision(20, 0);
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
            e.Property(x => x.EstateAmount).HasPrecision(20, 0);
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
        });

        modelBuilder.Entity<MoneyLog>(e =>
        {
            e.ToTable("money_log");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Id, x.Uuid, x.Player });
            e.Property(x => x.Amount).HasPrecision(20, 0);
        });

        modelBuilder.Entity<ServerEstateHistory>(e =>
        {
            e.ToTable("server_estate_history");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Year, x.Month, x.Day, x.Hour });
            e.Property(x => x.Vault).HasPrecision(20, 0);
            e.Property(x => x.Bank).HasPrecision(20, 0);
            e.Property(x => x.Cash).HasPrecision(20, 0);
            e.Property(x => x.EstateAmount).HasPrecision(20, 0);
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
            e.Property(x => x.BorrowAmount).HasPrecision(20, 0);
            e.Property(x => x.PaymentAmount).HasPrecision(20, 0);
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

