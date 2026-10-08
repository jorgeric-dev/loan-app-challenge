using LoanApp.Application;
using LoanApp.Application.Abstractions;
using LoanApp.Application.Events;
using LoanApp.Domain.Decisions;
using LoanApp.Domain.Decisions.Rules;
using Microsoft.Extensions.DependencyInjection;

namespace LoanApp.UnitTests;

/// <summary>Open/closed in practice: rules and event handlers are picked up without editing any registration.</summary>
public class DependencyInjectionTests
{
    private readonly IServiceCollection _services = new ServiceCollection().AddApplication();

    [Fact]
    public void Every_decision_rule_in_the_domain_is_registered() =>
        Assert.Equal(
            new[] { typeof(BlacklistedSsnRule), typeof(RestrictedStateRule) }.OrderBy(t => t.Name),
            _services.Where(d => d.ServiceType == typeof(IDecisionRule)).Select(d => d.ImplementationType!).OrderBy(t => t.Name));

    [Fact]
    public void Integration_event_handlers_are_registered_by_contract() =>
        Assert.Contains(_services, d =>
            d.ServiceType == typeof(IIntegrationEventHandler<LoanApplicationApproved>) &&
            d.ImplementationType == typeof(LoanApplicationApprovedHandler));
}
