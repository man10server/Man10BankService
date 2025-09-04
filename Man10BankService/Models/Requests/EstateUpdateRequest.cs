using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Requests;

public class EstateUpdateRequest
{
    // 任意: 更新したいものだけ指定する
    public decimal? Cash { get; set; }
    public decimal? Vault { get; set; }
    public decimal? EstateAmount { get; set; }
    public decimal? Shop { get; set; }
}

