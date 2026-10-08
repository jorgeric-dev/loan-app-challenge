namespace LoanApp.Application.Abstractions;

/// <summary>Operational view of background event delivery: backlog, dead letters and replay (production recovery).</summary>
public interface IEventDeliveryOperations
{
    Task<EventDeliveryStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<DeadLetter>> ListDeadLettersAsync(CancellationToken cancellationToken);

    /// <summary>Puts a dead-lettered event back in the queue. Returns false if it is not dead-lettered.</summary>
    Task<bool> RequeueAsync(long id, CancellationToken cancellationToken);

    /// <summary>Puts every dead-lettered event back in the queue (e.g. after an outage of the external service is fixed).</summary>
    Task<int> RequeueAllAsync(CancellationToken cancellationToken);
}

public sealed record EventDeliveryStatus(int Pending, int DeadLettered, TimeSpan? OldestPendingAge);

public sealed record DeadLetter(
    long Id,
    Guid EventId,
    string Type,
    string OrderingKey,
    int Attempts,
    string? LastError,
    DateTimeOffset OccurredAt,
    DateTimeOffset FailedAt);
