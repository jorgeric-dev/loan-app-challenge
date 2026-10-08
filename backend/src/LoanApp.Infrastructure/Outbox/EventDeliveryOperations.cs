using LoanApp.Application.Abstractions;
using LoanApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LoanApp.Infrastructure.Outbox;

public sealed class EventDeliveryOperations(LoanDbContext db, TimeProvider time) : IEventDeliveryOperations
{
    public async Task<EventDeliveryStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var unprocessed = db.OutboxMessages.Where(m => m.ProcessedAt == null);
        var pending = await unprocessed.CountAsync(m => m.FailedAt == null, cancellationToken);
        var deadLettered = await unprocessed.CountAsync(m => m.FailedAt != null, cancellationToken);
        var oldest = await unprocessed.Where(m => m.FailedAt == null)
            .OrderBy(m => m.Id).Select(m => (DateTimeOffset?)m.OccurredAt).FirstOrDefaultAsync(cancellationToken);
        return new EventDeliveryStatus(pending, deadLettered, time.GetUtcNow() - oldest);
    }

    public async Task<IReadOnlyList<DeadLetter>> ListDeadLettersAsync(CancellationToken cancellationToken) =>
        await DeadLetters()
            .OrderBy(m => m.Id)
            .Select(m => new DeadLetter(m.Id, m.EventId, m.Type, m.OrderingKey, m.Attempts, m.LastError, m.OccurredAt, m.FailedAt!.Value))
            .ToListAsync(cancellationToken);

    public async Task<bool> RequeueAsync(long id, CancellationToken cancellationToken) =>
        await RequeueAsync(DeadLetters().Where(m => m.Id == id), cancellationToken) == 1;

    public Task<int> RequeueAllAsync(CancellationToken cancellationToken) =>
        RequeueAsync(DeadLetters(), cancellationToken);

    private IQueryable<OutboxMessage> DeadLetters() =>
        db.OutboxMessages.Where(m => m.ProcessedAt == null && m.FailedAt != null);

    // A requeued message gets a fresh retry budget; LastError is kept for the record.
    private Task<int> RequeueAsync(IQueryable<OutboxMessage> messages, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return messages.ExecuteUpdateAsync(set => set
            .SetProperty(m => m.FailedAt, (DateTimeOffset?)null)
            .SetProperty(m => m.Attempts, 0)
            .SetProperty(m => m.NextAttemptAt, now), cancellationToken);
    }
}
