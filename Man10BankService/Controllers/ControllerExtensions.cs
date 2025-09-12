using Man10BankService.Services;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

public static class ControllerExtensions
{
    public static ActionResult ToProblem<T>(this ControllerBase controller, ApiResult<T> res)
    {
        var pd = new ProblemDetails
        {
            Title = res.Code.ToString(),
            Type = res.Code.ToString(),
            Status = res.StatusCode,
        };
        return controller.StatusCode(res.StatusCode, pd);
    }
}

