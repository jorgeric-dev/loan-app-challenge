using System.Text.Json;
using LoanApp.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LoanApp.Infrastructure.Outbox;

/// <summary>
/// Routes a stored message to its <see cref="IIntegrationEventHandler{TEvent}"/> by event type name.
/// Routes are built once at startup from the registered handlers, so a new event needs no change here.
/// </summary>
public sealed class IntegrationEventDispatcher(IEnumerable<IOutboxRoute> routes)
{
    private readonly Dictionary<string, IOutboxRoute> _routes = routes.ToDictionary(r => r.EventType);

    public Task DispatchAsync(IServiceProvider services, OutboxMessage message, CancellationToken cancellationToken) =>
        _routes.TryGetValue(message.Type, out var route)
            ? route.DispatchAsync(services, message.Payload, cancellationToken)
            : throw new PermanentFailureException($"No handler registered for event type '{message.Type}'.");

    public static string EventTypeName(Type eventType) => eventType.Name;
}

public interface IOutboxRoute
{
    string EventType { get; }

    Task DispatchAsync(IServiceProvider services, string payload, CancellationToken cancellationToken);
}

public sealed class OutboxRoute<TEvent> : IOutboxRoute where TEvent : IIntegrationEvent
{
    public string EventType => IntegrationEventDispatcher.EventTypeName(typeof(TEvent));

    public Task DispatchAsync(IServiceProvider services, string payload, CancellationToken cancellationToken)
    {
        var integrationEvent = JsonSerializer.Deserialize<TEvent>(payload)
            ?? throw new PermanentFailureException($"Payload of {EventType} is empty.");
        return services.GetRequiredService<IIntegrationEventHandler<TEvent>>().HandleAsync(integrationEvent, cancellationToken);
    }
}
