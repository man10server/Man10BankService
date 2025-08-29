namespace Man10BankService.Services;

public enum ResultCode
{
    Ok = 0,
    InvalidArgument = 1,
    NotFound = 2,
    InsufficientFunds = 3,
    Error = 99,
}

public sealed record Result<T>(ResultCode Code, string Message = "", T? Data = default)
{
    public static Result<T> Ok(T data) => new(ResultCode.Ok, "", data);
    public static Result<T> Fail(ResultCode code, string message) => new(code, message, default);
}

