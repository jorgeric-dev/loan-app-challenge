namespace LoanApp.Application.Abstractions;

/// <summary>Commits every pending change (customer, application and published events) atomically.</summary>
public interface IUnitOfWork
{
    /// <exception cref="ConcurrencyConflictException">Another request changed the same data first.</exception>
    Task CommitAsync(CancellationToken cancellationToken);
}
