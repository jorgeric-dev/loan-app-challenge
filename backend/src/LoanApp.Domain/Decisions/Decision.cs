namespace LoanApp.Domain.Decisions;

public sealed record Decision(IReadOnlyList<DenialReason> DenialReasons)
{
    public bool IsApproved => DenialReasons.Count == 0;
}
