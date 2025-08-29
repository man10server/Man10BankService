namespace Man10BankService.Services;

public sealed record ApiResult<T>(int StatusCode, string Message = "", T? Data = default)
{
    public static ApiResult<T> Ok(T data, string message = "") => new(200, message, data);
    public static ApiResult<T> BadRequest(string message) => new(400, message);
    public static ApiResult<T> NotFound(string message) => new(404, message);
    public static ApiResult<T> Conflict(string message) => new(409, message);
    public static ApiResult<T> Error(string message) => new(500, message);
}

