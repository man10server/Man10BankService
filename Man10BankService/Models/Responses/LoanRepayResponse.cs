namespace Man10BankService.Models.Responses;

public enum LoanRepayOutcome
{
    Paid,
    CollateralCollected
}

public sealed record LoanRepayResponse(
    int LoanId,
    LoanRepayOutcome Outcome,
    decimal CollectedAmount,
    decimal RemainingAmount,
    string? CollateralItem
);

