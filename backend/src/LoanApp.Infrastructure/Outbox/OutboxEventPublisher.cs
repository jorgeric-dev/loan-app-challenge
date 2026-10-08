using System.Diagnostics;
using System.Text.Json;
using LoanApp.Application.Abstractions;
using LoanApp.Infrastructure.Persistence;

namespace LoanApp.Infrastructure.Outbox;

/// <summary>
/// Transactional outbox: the event is stored as a row in the same DbContext as the business data,
/// so it is committed (or rolled back) by the same transaction. The current trace id travels with it.
/// </summary>
public sealed class OutboxEventPublisher(LoanDbContext db, TimeProvider time) : IEventPublisher
{
    public void Publish<TEvent>(TEvent integrationEvent) where TEvent : IIntegrationEvent =>
        db.OutboxMessages.Add(new OutboxMessage(
            integrationEvent.EventId,
            IntegrationEventDispatcher.EventTypeName(typeof(TEvent)),
            JsonSerializer.Serialize(integrationEvent),
            integrationEvent.OrderingKey,
            Activity.Current?.Id,
            time.GetUtcNow()));
}
