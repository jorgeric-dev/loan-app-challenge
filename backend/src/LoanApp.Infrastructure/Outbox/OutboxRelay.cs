using System.Diagnostics;
using LoanApp.Application.Diagnostics;
using LoanApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LoanApp.Infrastructure.Outbox;

/// <summary>
/// Claims a batch of due messages and delivers them. Safe to run on several instances at once:
/// <list type="bullet">
/// <item>the claim is one atomic UPDATE with UPDLOCK/READPAST, so two workers never claim the same message;</item>
/// <item>a message is only claimable when no earlier message with the same ordering key is still unprocessed
/// (pending, leased or dead-lettered), so per-customer order is kept while different customers proceed in parallel;</item>
/// <item>claims are leases: if a worker dies mid-delivery the lease expires and another worker redelivers (at-least-once,
/// made safe downstream by the idempotency key).</item>
/// </list>
/// </summary>
public sealed class OutboxRelay(
    LoanDbContext db,
    IServiceProvider services,
    IntegrationEventDispatcher dispatcher,
    OutboxRetryPolicy retryPolicy,
    OutboxMetrics metrics,
    IOptions<OutboxOptions> options,
    TimeProvider time,
    ILogger<OutboxRelay> logger)
{
    private static readonly string WorkerId = $"{Environment.MachineName}:{Environment.ProcessId}";

    /// <returns>How many messages were claimed (0 = nothing due right now).</returns>
    public async Task<int> RelayBatchAsync(CancellationToken cancellationToken)
    {
        var batch = await ClaimAsync(cancellationToken);
        foreach (var message in batch)
        {
            if (cancellationToken.IsCancellationRequested)
                message.ReleaseLease();
            else
                await DeliverAsync(message, cancellationToken);

            // Persist each outcome immediately, even while shutting down, so no work is lost or repeated needlessly.
            await db.SaveChangesAsync(CancellationToken.None);
        }
        return batch.Count;
    }

    private Task<List<OutboxMessage>> ClaimAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();
        var leaseUntil = now + settings.LeaseDuration;
        return db.OutboxMessages.FromSql($"""
            WITH next AS (
                SELECT TOP ({settings.BatchSize}) *
                FROM OutboxMessages m WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE m.ProcessedAt IS NULL AND m.FailedAt IS NULL
                  AND m.NextAttemptAt <= {now}
                  AND (m.LockedUntil IS NULL OR m.LockedUntil < {now})
                  AND NOT EXISTS (
                      SELECT 1 FROM OutboxMessages earlier
                      WHERE earlier.OrderingKey = m.OrderingKey AND earlier.Id < m.Id AND earlier.ProcessedAt IS NULL)
                ORDER BY m.Id)
            UPDATE next SET LockedUntil = {leaseUntil}, LockedBy = {WorkerId}
            OUTPUT inserted.*;
            """).ToListAsync(cancellationToken);
    }

    private async Task DeliverAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        using var activity = StartDeliveryActivity(message);
        using var _ = logger.BeginScope(new Dictionary<string, object?>
        {
            ["OutboxMessageId"] = message.Id,
            ["EventId"] = message.EventId,
            ["EventType"] = message.Type,
            ["OrderingKey"] = message.OrderingKey,
        });

        try
        {
            await dispatcher.DispatchAsync(services, message, cancellationToken);
            var now = time.GetUtcNow();
            message.MarkProcessed(now);
            metrics.Delivered(message, now);
            logger.LogInformation("Delivered {EventType} {EventId} after {Attempts} attempt(s)", message.Type, message.EventId, message.Attempts);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            message.ReleaseLease(); // shutting down: another worker (or this one after restart) picks it up
        }
        catch (Exception ex)
        {
            var now = time.GetUtcNow();
            var retryAt = retryPolicy.NextAttempt(message.Attempts + 1, ex, now);
            if (retryAt is null)
            {
                message.MoveToDeadLetter(ex.Message, now);
                logger.LogError(ex, "Gave up delivering {EventType} {EventId} after {Attempts} attempt(s); moved to dead letter",
                    message.Type, message.EventId, message.Attempts);
            }
            else
            {
                message.ScheduleRetry(ex.Message, retryAt.Value);
                logger.LogWarning(ex, "Delivery of {EventType} {EventId} failed (attempt {Attempts}); retrying at {RetryAt:O}",
                    message.Type, message.EventId, message.Attempts, retryAt);
            }
            metrics.Failed(message, deadLettered: retryAt is null);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
        }
    }

    /// <summary>Continues the trace of the HTTP request that published the event.</summary>
    private static Activity? StartDeliveryActivity(OutboxMessage message)
    {
        ActivityContext.TryParse(message.TraceParent, null, out var parent);
        return LoanAppTelemetry.ActivitySource.StartActivity(
            $"{message.Type} process",
            ActivityKind.Consumer,
            parent,
            [
                new("messaging.system", "outbox"),
                new("messaging.operation.type", "process"),
                new("messaging.message.id", message.EventId.ToString()),
                new("outbox.message_id", message.Id),
                new("outbox.attempt", message.Attempts + 1),
            ]);
    }
}
