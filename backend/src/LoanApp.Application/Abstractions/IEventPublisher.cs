namespace LoanApp.Application.Abstractions;

/// <summary>
/// Publishes an integration event as part of the current unit of work: the event is only
/// released when <see cref="IUnitOfWork.CommitAsync"/> succeeds, and is discarded otherwise.
/// It is then handled in the background, outside the HTTP request.
/// </summary>
public interface IEventPublisher
{
    void Publish<TEvent>(TEvent integrationEvent) where TEvent : IIntegrationEvent;
}
