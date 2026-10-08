namespace LoanApp.Infrastructure.Outbox;

/// <summary>
/// An integration event stored in the same transaction as the business data, delivered later by <see cref="OutboxRelay"/>.
/// Lifecycle: pending → (claimed by a worker under a lease) → processed | retry scheduled | dead-lettered.
/// </summary>
public sealed class OutboxMessage
{
    public long Id { get; private set; }
    public Guid EventId { get; private set; }
    public string Type { get; private set; } = null!;
    public string Payload { get; private set; } = null!;

    /// <summary>Messages sharing this key are delivered in <see cref="Id"/> order (one customer's events never overtake each other).</summary>
    public string OrderingKey { get; private set; } = null!;

    /// <summary>W3C trace context of the request that published the event, so the background delivery joins the same trace.</summary>
    public string? TraceParent { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Worker lease: while in the future, the message belongs to <see cref="LockedBy"/>. An expired lease (crashed worker) is reclaimed.</summary>
    public DateTimeOffset? LockedUntil { get; private set; }
    public string? LockedBy { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>Set when delivery was abandoned (dead letter). It blocks later messages with the same ordering key until requeued.</summary>
    public DateTimeOffset? FailedAt { get; private set; }

    private OutboxMessage() { } // EF Core

    public OutboxMessage(Guid eventId, string type, string payload, string orderingKey, string? traceParent, DateTimeOffset occurredAt)
    {
        EventId = eventId;
        Type = type;
        Payload = payload;
        OrderingKey = orderingKey;
        TraceParent = traceParent;
        OccurredAt = occurredAt;
        NextAttemptAt = occurredAt;
    }

    public void MarkProcessed(DateTimeOffset now)
    {
        Attempts++;
        ProcessedAt = now;
        ReleaseLease();
    }

    public void ScheduleRetry(string error, DateTimeOffset nextAttemptAt)
    {
        Attempts++;
        LastError = error;
        NextAttemptAt = nextAttemptAt;
        ReleaseLease();
    }

    public void MoveToDeadLetter(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error;
        FailedAt = now;
        ReleaseLease();
    }

    /// <summary>Hands a claimed message back without counting an attempt (graceful shutdown).</summary>
    public void ReleaseLease()
    {
        LockedUntil = null;
        LockedBy = null;
    }
}
