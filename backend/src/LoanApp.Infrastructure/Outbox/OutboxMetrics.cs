using System.Diagnostics.Metrics;
using LoanApp.Application.Diagnostics;

namespace LoanApp.Infrastructure.Outbox;

/// <summary>Delivery metrics. Alert on dead letters and on end-to-end lag (time from commit to delivery).</summary>
public sealed class OutboxMetrics
{
    private readonly Counter<long> _deliveries;
    private readonly Histogram<double> _lag;

    public OutboxMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(LoanAppTelemetry.Name);
        _deliveries = meter.CreateCounter<long>("loanapp.outbox.deliveries", description: "Delivery attempts, by event type and outcome (delivered, retry, dead_letter).");
        _lag = meter.CreateHistogram<double>("loanapp.outbox.delivery_lag", unit: "s", description: "Time from the event being committed to being delivered.");
    }

    public void Delivered(OutboxMessage message, DateTimeOffset now)
    {
        _deliveries.Add(1, new("event.type", message.Type), new("outcome", "delivered"));
        _lag.Record((now - message.OccurredAt).TotalSeconds, new KeyValuePair<string, object?>("event.type", message.Type));
    }

    public void Failed(OutboxMessage message, bool deadLettered) =>
        _deliveries.Add(1, new("event.type", message.Type), new("outcome", deadLettered ? "dead_letter" : "retry"));
}
