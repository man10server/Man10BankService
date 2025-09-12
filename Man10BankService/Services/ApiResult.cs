namespace Man10BankService.Services;

public enum ErrorCode
{
    None = 0,
    ValidationError,
    NotFound,
    Conflict,
    UnexpectedError,

    // Domain specific
    InsufficientFunds,
    LimitOutOfRange,
    OffsetOutOfRange,
    ChequeNotFound,
    ChequeAlreadyUsed,
    EstateNotFound,
    EstateUpdated,
    EstateNoChange,
    LoanNotFound,
    BorrowLimitExceeded,
    NoRepaymentNeeded,
    PaymentAmountNotSet,
    PaymentAmountZero,
    InterestStopped,
    InterestZero,
    BeforePaybackDate
}

public sealed record ApiResult<T>(int StatusCode, ErrorCode Code = ErrorCode.None, T? Data = default)
{
    public static ApiResult<T> Ok(T data, ErrorCode code = ErrorCode.None) => new(200, code, data);
    public static ApiResult<T> BadRequest(ErrorCode code) => new(400, code);
    public static ApiResult<T> NotFound(ErrorCode code) => new(404, code);
    public static ApiResult<T> Conflict(ErrorCode code) => new(409, code);
    public static ApiResult<T> Error(ErrorCode code) => new(500, code);
}
