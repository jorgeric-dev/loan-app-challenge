using System.Diagnostics.Metrics;

namespace LoanApp.Application.Diagnostics;

/// <summary>Business metrics: how many applications are approved/denied, why, and how many are returning customers.</summary>
public sealed class ApplicationMetrics
{
    private readonly Counter<long> _decisions;
    private readonly Counter<long> _denialReasons;

    public ApplicationMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(LoanAppTelemetry.Name);
        _decisions = meter.CreateCounter<long>("loanapp.applications.decisions", description: "Loan applications decided, by outcome.");
        _denialReasons = meter.CreateCounter<long>("loanapp.applications.denial_reasons", description: "Deny rules that matched, by reason code.");
    }

    public void Approved(bool isReturningCustomer) =>
        _decisions.Add(1, new("decision", "approved"), new("customer.type", isReturningCustomer ? "returning" : "new"));

    public void Denied(IEnumerable<string> reasonCodes)
    {
        _decisions.Add(1, new KeyValuePair<string, object?>("decision", "denied"));
        foreach (var code in reasonCodes)
            _denialReasons.Add(1, new KeyValuePair<string, object?>("reason", code));
    }
}
