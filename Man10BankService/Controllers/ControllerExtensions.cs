using Man10BankService.Services;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

public static class ControllerExtensions
{
    public static ActionResult<T> ToActionResult<T>(this ControllerBase controller, ApiResult<T> res)
    {
        if (res.StatusCode == 200)
        {
            return controller.Ok(res.Data);
        }

        var pd = new ProblemDetails
        {
            Title = res.Code.GetJa(),
            Type = res.Code.ToString(),
            Status = res.StatusCode,
        };
        pd.Extensions["code"] = res.Code.ToString();

        return res.StatusCode switch
        {
            400 => controller.BadRequest(pd),
            404 => controller.NotFound(pd),
            409 => controller.Conflict(pd),
            _ => controller.StatusCode(res.StatusCode, pd)
        };
    }
}
