namespace LoanApp.Application.Abstractions;

/// <summary>An event published to other systems through <see cref="IEventPublisher"/>.</summary>
public interface IIntegrationEvent
{
    /// <summary>Unique per event. Sent downstream as the idempotency key, so a redelivery is not applied twice.</summary>
    Guid EventId { get; }

    /// <summary>Events with the same key are delivered strictly in order (e.g. a customer's create before its update).</summary>
    string OrderingKey { get; }
}
