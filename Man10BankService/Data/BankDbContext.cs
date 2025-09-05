using Man10BankService.Models;
using Man10BankService.Models.Database;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Data;

public class BankDbContext : DbContext
{
    private static string? _connectionString;

    public static void Configure(IConfiguration configuration)
    {
        var db = configuration.GetSection("Database");
        var host = db["Host"] ?? "";
        var port = db["Port"] ?? "";
        var name = db["Name"] ?? "";
        var user = db["User"] ?? "";
        var password = db["Password"] ?? "";
        var treatTiny = (db["TreatTinyAsBoolean"] ?? "true").ToLowerInvariant();
        _connectionString = $"Server={host};Port={port};Database={name};User Id={user};Password={password};TreatTinyAsBoolean={treatTiny};";
    }
    
    public static string GetConnectionString()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException("データベース接続が未設定です。起動時に BankDbContext.Configure(...) を呼び出してください。");
        return _connectionString!;
    }
    
    public DbSet<AtmLog> AtmLogs => Set<AtmLog>();
    public DbSet<Cheque> Cheques => Set<Cheque>();
    public DbSet<Estate> Estates => Set<Estate>();
    public DbSet<EstateHistory> EstateHistories => Set<EstateHistory>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<MoneyLog> MoneyLogs => Set<MoneyLog>();
    public DbSet<ServerEstateHistory> ServerEstateHistories => Set<ServerEstateHistory>();
    public DbSet<ServerLoan> ServerLoans => Set<ServerLoan>();
    public DbSet<ServerLoanLog> ServerLoanLogs => Set<ServerLoanLog>();
    public DbSet<UserBank> UserBanks => Set<UserBank>();
    
    public BankDbContext() {}
    public BankDbContext(DbContextOptions<BankDbContext> options) : base(options) {}
    
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.IsConfigured)
            return;

        if (string.IsNullOrWhiteSpace(_connectionString)) 
            throw new InvalidOperationException("データベース接続が未設定です。起動時に BankDbContext.Configure(...) を呼び出してください。");

        optionsBuilder.UseMySql(_connectionString, ServerVersion.AutoDetect(_connectionString));
    }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AtmLog>(e =>
        {
            e.ToTable("atm_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(20, 0);
            e.Property(x => x.Deposit).HasDefaultValue(false);
            e.Property(x => x.Date).HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<Cheque>(e =>
        {
            e.ToTable("cheque_tbl");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Used);
            e.Property(x => x.Amount).HasPrecision(20, 0);
            e.Property(x => x.UseDate).HasColumnName("use_date");
            e.Property(x => x.UsePlayer).HasColumnName("use_player");
            e.Property(x => x.Note).HasDefaultValue("");
            e.Property(x => x.UsePlayer).HasDefaultValue("");
            e.Property(x => x.Used).HasDefaultValue(false);
            e.Property(x => x.Date).HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
            e.Property(x => x.UseDate).HasDefaultValueSql("CURRENT_TIMESTAMP");
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
            e.Property(x => x.Player).HasDefaultValue("");
            e.Property(x => x.Date).HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
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
            e.Property(x => x.Player).HasDefaultValue("");
            e.Property(x => x.Date).HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
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
            e.Property(x => x.LendPlayer).HasDefaultValue("");
            e.Property(x => x.LendUuid).HasDefaultValue("");
            e.Property(x => x.BorrowPlayer).HasDefaultValue("");
            e.Property(x => x.BorrowUuid).HasDefaultValue("");
            e.Property(x => x.BorrowDate).HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
            e.Property(x => x.PaybackDate).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.CollateralItem).HasDefaultValue("");
        });

        modelBuilder.Entity<MoneyLog>(e =>
        {
            e.ToTable("money_log");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Id, x.Uuid, x.Player });
            e.Property(x => x.Amount).HasPrecision(20, 0);
            e.Property(x => x.PluginName).HasColumnName("plugin_name");
            e.Property(x => x.DisplayNote).HasColumnName("display_note");
            e.Property(x => x.PluginName).HasDefaultValue("");
            e.Property(x => x.Note).HasDefaultValue("");
            e.Property(x => x.DisplayNote).HasDefaultValue("");
            e.Property(x => x.Server).HasDefaultValue("");
            e.Property(x => x.Deposit).HasDefaultValue(true);
            e.Property(x => x.Date).HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
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
            e.Property(x => x.Year).HasDefaultValue(0);
            e.Property(x => x.Month).HasDefaultValue(0);
            e.Property(x => x.Day).HasDefaultValue(0);
            e.Property(x => x.Hour).HasDefaultValue(0);
            e.Property(x => x.Date).HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
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
            e.Property(x => x.Player).HasDefaultValue("");
            e.Property(x => x.Uuid).HasDefaultValue("");
            e.Property(x => x.BorrowDate).HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
            e.Property(x => x.LastPayDate).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.FailedPayment).HasDefaultValue(0);
            e.Property(x => x.StopInterest).HasDefaultValue(false);
        });

        modelBuilder.Entity<ServerLoanLog>(e =>
        {
            e.ToTable("server_loan_log");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Uuid, x.Date });
            e.Property(x => x.Amount).HasPrecision(20, 0);
            e.Property(x => x.Player).HasDefaultValue("");
            e.Property(x => x.Uuid).HasDefaultValue("");
            e.Property(x => x.Date).HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
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
