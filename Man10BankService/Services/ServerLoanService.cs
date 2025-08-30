using Man10BankService.Data;
using Man10BankService.Models;
using Man10BankService.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Man10BankService.Services;

public class ServerLoanService(IDbContextFactory<BankDbContext> dbFactory)
{
    private static decimal DailyInterestRate { get; set; } = 0.001m;
    private static decimal MinAmount { get; set; } = 100000m;
    private static decimal MaxAmount { get; set; } = 3000000m;

    public static void Configure(IConfiguration configuration)
    {
        var s = configuration.GetSection("ServerLoan");
        DailyInterestRate = s.GetValue("DailyInterestRate", 0.001m);
        MinAmount = s.GetValue("MinAmount", 100000m);
        MaxAmount = s.GetValue("MaxAmount", 3000000m);
    }

    public async Task<ApiResult<ServerLoan>> GetByUuidAsync(string uuid)
    {
        try
        {
            var repo = new ServerLoanRepository(dbFactory);
            var loan = await repo.GetByUuidAsync(uuid);
            if (loan == null)
                return ApiResult<ServerLoan>.NotFound("借入データが見つかりません。");
            return ApiResult<ServerLoan>.Ok(loan);
        }
        catch (Exception ex)
        {
            return ApiResult<ServerLoan>.Error($"借入データの取得に失敗しました: {ex.Message}");
        }
    }
}
