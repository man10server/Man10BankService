namespace Man10BankService.Services;

public static class ErrorCodeMessages
{
    public static string Get(ErrorCode code)
    {
        return code switch
        {
            ErrorCode.None => "エラーはありません。",
            ErrorCode.ValidationError => "入力内容が不正です。",
            ErrorCode.NotFound => "対象が見つかりません。",
            ErrorCode.PlayerNotFound => "プレイヤーが見つかりません。",
            ErrorCode.Conflict => "処理が競合しました。",
            ErrorCode.UnexpectedError => "予期しないエラーが発生しました。",
            ErrorCode.InsufficientFunds => "残高が不足しています。",
            ErrorCode.LimitOutOfRange => "limit は 1 から 1000 の範囲で指定してください。",
            ErrorCode.OffsetOutOfRange => "offset は 0 以上で指定してください。",
            ErrorCode.ChequeNotFound => "小切手が見つかりません。",
            ErrorCode.ChequeAlreadyUsed => "小切手はすでに使用済みです。",
            ErrorCode.EstateNotFound => "資産情報が見つかりません。",
            ErrorCode.EstateUpdated => "資産情報を更新しました。",
            ErrorCode.EstateNoChange => "資産情報の変更はありませんでした。",
            ErrorCode.LoanNotFound => "貸付情報が見つかりません。",
            ErrorCode.LoanNotRepaid => "返済が完了していません。",
            ErrorCode.CollateralNotFound => "担保が設定されていません。",
            ErrorCode.CollateralAlreadyReleased => "担保はすでに解放済みです。",
            ErrorCode.BorrowLimitExceeded => "借入上限を超えています。",
            ErrorCode.NoRepaymentNeeded => "返済はすでに完了しています。",
            ErrorCode.PaymentAmountNotSet => "返済金額が未設定です。",
            ErrorCode.PaymentAmountZero => "返済金額が 0 です。",
            ErrorCode.InterestStopped => "利息加算は停止中です。",
            ErrorCode.InterestZero => "利息は 0 です。",
            ErrorCode.BeforePaybackDate => "返済期日より前のため回収できません。",
            ErrorCode.LenderUuidRequired => "貸手UUIDを指定してください。",
            ErrorCode.BorrowerUuidRequired => "借手UUIDを指定してください。",
            ErrorCode.CollectorUuidRequired => "回収者UUIDを指定してください。",
            ErrorCode.LenderAndBorrowerMustDiffer => "貸手と借手に同じUUIDは指定できません。",
            ErrorCode.BorrowAmountMustBePositive => "借入金額は 0 より大きく指定してください。",
            ErrorCode.RepayAmountMustBePositive => "返済金額は 0 より大きく指定してください。",
            ErrorCode.RepayAmountMustExceedBorrowAmount => "返済金額は借入金額より大きく指定してください。",
            ErrorCode.BorrowerMismatch => "借手UUIDが貸付情報と一致しません。",
            _ => "不明なエラーが発生しました。"
        };
    }
}
