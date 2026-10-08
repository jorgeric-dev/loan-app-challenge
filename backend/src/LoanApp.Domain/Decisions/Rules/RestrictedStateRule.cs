namespace LoanApp.Domain.Decisions.Rules;

/// <summary>Denies applicants living in New York.</summary>
public sealed class RestrictedStateRule : IDecisionRule
{
    public const string Code = "STATE_NOT_SERVED";
    private const string RestrictedState = "NY";

    public DenialReason? Evaluate(LoanRequest request) =>
        request.Applicant.Address.State == RestrictedState
            ? new DenialReason(Code, $"Applicant state {RestrictedState} is not served.")
            : null;
}
