namespace LoanApp.Application.Abstractions;

/// <summary>
/// Handles an integration event in the background. Implementations are discovered automatically,
/// so adding an event means adding an event type and a handler; nothing else changes.
/// Throw <see cref="PermanentFailureException"/> when retrying cannot succeed; any other exception is retried.
/// </summary>
public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
