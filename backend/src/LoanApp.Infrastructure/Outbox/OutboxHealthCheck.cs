using LoanApp.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace LoanApp.Infrastructure.Outbox;

/// <summary>
/// Degraded (never Unhealthy) when events pile up or are dead-lettered: the API can still take applications,
/// so this must not take the instance out of the load balancer — it is a signal for alerting.
/// </summary>
public sealed class OutboxHealthCheck(IEventDeliveryOperations operations, IOptions<OutboxOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = await operations.GetStatusAsync(cancellationToken);
        var data = new Dictionary<string, object>
        {
            ["pending"] = status.Pending,
            ["deadLettered"] = status.DeadLettered,
            ["oldestPendingAgeSeconds"] = (int)(status.OldestPendingAge?.TotalSeconds ?? 0),
        };

        if (status.DeadLettered > 0)
            return HealthCheckResult.Degraded($"{status.DeadLettered} event(s) in the dead-letter queue.", data: data);
        if (status.OldestPendingAge > options.Value.MaxHealthyBacklogAge)
            return HealthCheckResult.Degraded($"Oldest pending event is {status.OldestPendingAge:g} old.", data: data);
        return HealthCheckResult.Healthy(data: data);
    }
}
