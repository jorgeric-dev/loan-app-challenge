using LoanApp.Domain;
using LoanApp.Domain.Decisions;
using LoanApp.Domain.Decisions.Rules;

namespace LoanApp.UnitTests;

public class DecisionEngineTests
{
    private sealed class FixedBlacklist(params string[] ssns) : ISsnBlacklist
    {
        public bool Contains(Ssn ssn) => ssns.Select(Ssn.Parse).Contains(ssn);
    }

    private static readonly DecisionEngine Engine = new(
    [
        new RestrictedStateRule(),
        new BlacklistedSsnRule(new FixedBlacklist("123-45-6789")),
    ]);

    [Fact]
    public void Approves_when_no_rule_denies()
    {
        var decision = Engine.Decide(TestData.Request(ssn: "222-33-4444", state: "TX"));

        Assert.True(decision.IsApproved);
        Assert.Empty(decision.DenialReasons);
    }

    [Theory]
    [InlineData("NY")]
    [InlineData("ny")]
    public void Denies_applicants_from_new_york(string state)
    {
        var decision = Engine.Decide(TestData.Request(state: state));

        Assert.False(decision.IsApproved);
        Assert.Equal(RestrictedStateRule.Code, Assert.Single(decision.DenialReasons).Code);
    }

    [Theory]
    [InlineData("123-45-6789")]
    [InlineData("123456789")]
    public void Denies_blacklisted_ssn_in_any_format(string ssn)
    {
        var decision = Engine.Decide(TestData.Request(ssn: ssn));

        Assert.False(decision.IsApproved);
        Assert.Equal(BlacklistedSsnRule.Code, Assert.Single(decision.DenialReasons).Code);
    }

    [Fact]
    public void Reports_every_matching_rule()
    {
        var decision = Engine.Decide(TestData.Request(ssn: "123-45-6789", state: "NY"));

        Assert.Equal(
            [RestrictedStateRule.Code, BlacklistedSsnRule.Code],
            decision.DenialReasons.Select(reason => reason.Code));
    }

    [Fact]
    public void New_rules_plug_in_without_touching_existing_ones()
    {
        var engine = new DecisionEngine([new RestrictedStateRule(), new MaxAmountRule()]);

        var decision = engine.Decide(TestData.Request(amount: 2_000_000));

        Assert.Equal("AMOUNT_TOO_HIGH", Assert.Single(decision.DenialReasons).Code);
    }

    private sealed class MaxAmountRule : IDecisionRule
    {
        public DenialReason? Evaluate(LoanRequest request) =>
            request.RequestedAmount > 1_000_000 ? new DenialReason("AMOUNT_TOO_HIGH", "Over the limit.") : null;
    }
}
