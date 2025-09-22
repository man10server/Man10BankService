namespace Man10BankService.Models.Responses;

public sealed record PaymentInfoResponse(DateTime NextRepayDate, decimal DailyInterestPerDay);

