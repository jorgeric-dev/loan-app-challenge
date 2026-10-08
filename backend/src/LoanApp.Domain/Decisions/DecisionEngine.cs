namespace LoanApp.Domain.Decisions;

/// <summary>
/// Runs every rule and approves only when none of them denies.
/// All rules are evaluated (no short-circuit) so the decision lists every reason.
/// </summary>
public sealed class DecisionEngine(IEnumerable<IDecisionRule> rules)
{
    public Decision Decide(LoanRequest request) =>
        new(rules.Select(rule => rule.Evaluate(request)).OfType<DenialReason>().ToList());
}
