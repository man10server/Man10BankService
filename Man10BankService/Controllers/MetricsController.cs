using System.Globalization;
using System.Text;
using Man10BankService.Data;
using Man10BankService.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Controllers;

[ApiController]
[Route("metrics")]
public class MetricsController(IDbContextFactory<BankDbContext> dbFactory) : ControllerBase
{
    [HttpGet]
    [Produces("text/plain")]
    public async Task<IActionResult> Get()
    {
        var repo = new ServerEstateRepository(dbFactory);
        var list = await repo.GetHistoryAsync(1, 0);
        var s = list.FirstOrDefault();

        var sb = new StringBuilder();

        void AppendGauge(string name, string help, string value)
        {
            sb.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
            sb.Append("# TYPE ").Append(name).Append(" gauge\n");
            sb.Append(name).Append(' ').Append(value).Append('\n');
        }

        string D(decimal v) => v.ToString(CultureInfo.InvariantCulture);
        string L(long v) => v.ToString(CultureInfo.InvariantCulture);
        string F(double v) => v.ToString("0.####################", CultureInfo.InvariantCulture);

        var vault = s?.Vault ?? 0m;
        var bank = s?.Bank ?? 0m;
        var cash = s?.Cash ?? 0m;
        var estateAmount = s?.EstateAmount ?? 0m;
        var loan = s?.Loan ?? 0m;
        var crypto = s?.Crypto ?? 0m;
        var shop = s?.Shop ?? 0m;
        var total = s?.Total ?? 0m;
        var year = s?.Year ?? 0;
        var month = s?.Month ?? 0;
        var day = s?.Day ?? 0;
        var hour = s?.Hour ?? 0;
        var tsSec = s is null ? 0d : (s.Date.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds;

        AppendGauge("man10_server_estate_vault", "Sum of vault", D(vault));
        AppendGauge("man10_server_estate_bank", "Sum of bank", D(bank));
        AppendGauge("man10_server_estate_cash", "Sum of cash", D(cash));
        AppendGauge("man10_server_estate_estate_amount", "Sum of estate", D(estateAmount));
        AppendGauge("man10_server_estate_loan", "Sum of loan", D(loan));
        AppendGauge("man10_server_estate_crypto", "Sum of crypto", D(crypto));
        AppendGauge("man10_server_estate_shop", "Sum of shop", D(shop));
        AppendGauge("man10_server_estate_total", "Sum of total", D(total));

        AppendGauge("man10_server_estate_year", "Snapshot year", L(year));
        AppendGauge("man10_server_estate_month", "Snapshot month", L(month));
        AppendGauge("man10_server_estate_day", "Snapshot day", L(day));
        AppendGauge("man10_server_estate_hour", "Snapshot hour", L(hour));
        AppendGauge("man10_server_estate_timestamp_seconds", "Snapshot unix time seconds", F(tsSec));

        return Content(sb.ToString(), "text/plain; version=0.0.4; charset=utf-8");
    }
}
