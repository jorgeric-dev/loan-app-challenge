namespace LoanApp.Domain.Decisions;

/// <summary>
/// A single deny rule. Returns a <see cref="DenialReason"/> when the request must be denied, otherwise null.
/// To add a rule, implement this interface and register it; existing rules are not touched.
/// </summary>
public interface IDecisionRule
{
    DenialReason? Evaluate(LoanRequest request);
}
