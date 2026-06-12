using Man10BankService.Services;
using Microsoft.AspNetCore.Mvc;

namespace Man10BankService.Controllers;

public static class ControllerExtensions
{
    // 成功時は 200 OK + Data、失敗時は ErrorCodeMapper で決まる HTTP ステータス + ProblemDetails を返す。
    public static ActionResult<T> ToActionResult<T>(this ControllerBase controller, ApiResult<T> res)
    {
        if (res.IsSuccess)
        {
            return controller.Ok(res.Data);
        }

        return controller.ToErrorResult<T>(res.Code);
    }

    // ErrorCode から ProblemDetails(title=日本語文言・extensions.code=ErrorCode名)を構築して返す。
    // type には enum 名を入れず、既定(about:blank 相当)のままにする。
    public static ActionResult<T> ToErrorResult<T>(this ControllerBase controller, ErrorCode code)
    {
        var status = ErrorCodeMapper.ToHttpStatus(code);
        var pd = new ProblemDetails
        {
            Title = code.GetJa(),
            Status = status
        };
        pd.Extensions["code"] = code.ToString();

        return status switch
        {
            400 => controller.BadRequest(pd),
            404 => controller.NotFound(pd),
            409 => controller.Conflict(pd),
            _ => controller.StatusCode(status, pd)
        };
    }
}
