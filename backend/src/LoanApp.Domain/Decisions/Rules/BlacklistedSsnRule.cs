namespace LoanApp.Domain.Decisions.Rules;

/// <summary>Denies applicants whose SSN is on the blacklist.</summary>
public sealed class BlacklistedSsnRule(ISsnBlacklist blacklist) : IDecisionRule
{
    public const string Code = "SSN_BLACKLISTED";

    public DenialReason? Evaluate(LoanRequest request) =>
        blacklist.Contains(request.Applicant.Ssn)
            ? new DenialReason(Code, "Applicant SSN is blacklisted.")
            : null;
}
