namespace Man10BankService.Services;

// ErrorCode から HTTP ステータスコードへの対応を一元管理する。
// 規約: PlayerNotFound/各種 NotFound は 404、業務的失敗(残高不足・使用済み・上限超過等)は 409、
//       入力不正は 400、予期せぬ例外のみ 500。
public static class ErrorCodeMapper
{
    public static int ToHttpStatus(ErrorCode code) => code switch
    {
        // 入力不正(400)
        ErrorCode.ValidationError => 400,
        ErrorCode.LimitOutOfRange => 400,
        ErrorCode.OffsetOutOfRange => 400,
        ErrorCode.LenderUuidRequired => 400,
        ErrorCode.BorrowerUuidRequired => 400,
        ErrorCode.CollectorUuidRequired => 400,
        ErrorCode.LenderAndBorrowerMustDiffer => 400,
        ErrorCode.BorrowAmountMustBePositive => 400,
        ErrorCode.BorrowAmountMustBeZeroOrGreater => 400,
        ErrorCode.RepayAmountMustBePositive => 400,
        ErrorCode.RepayAmountMustExceedBorrowAmount => 400,
        ErrorCode.BorrowerMismatch => 400,
        ErrorCode.NoRepaymentNeeded => 400,
        ErrorCode.PaymentAmountNotSet => 400,
        ErrorCode.PaymentAmountZero => 400,

        // 見つからない(404)
        ErrorCode.NotFound => 404,
        ErrorCode.PlayerNotFound => 404,
        ErrorCode.ChequeNotFound => 404,
        ErrorCode.EstateNotFound => 404,
        ErrorCode.LoanNotFound => 404,

        // 業務的失敗・競合(409)
        ErrorCode.Conflict => 409,
        ErrorCode.InsufficientFunds => 409,
        ErrorCode.ChequeAlreadyUsed => 409,
        ErrorCode.BorrowLimitExceeded => 409,
        ErrorCode.LoanNotRepaid => 409,
        ErrorCode.CollateralNotFound => 409,
        ErrorCode.CollateralAlreadyReleased => 409,
        ErrorCode.BeforePaybackDate => 409,
        ErrorCode.BalanceLimitExceeded => 409,

        // 予期せぬエラー・補償失敗(500)
        ErrorCode.UnexpectedError => 500,
        ErrorCode.SetBorrowAmountFailed => 500,
        ErrorCode.VaultConfigInvalid => 500,

        // それ以外(成功に付随する補足コード等が誤って渡された場合)は 500 とする
        _ => 500
    };
}
