using LoanApp.Application.Events;

namespace LoanApp.Application.Abstractions;

/// <summary>
/// The external system that mirrors our customers. Implementations throw on failure:
/// <see cref="PermanentFailureException"/> when a retry cannot help, any other exception when it can.
/// </summary>
public interface IExternalCustomerService
{
    Task CreateAsync(CustomerSnapshot customer, ApplicationSnapshot application, Guid idempotencyKey, CancellationToken cancellationToken);

    Task UpdateAsync(CustomerSnapshot customer, ApplicationSnapshot application, Guid idempotencyKey, CancellationToken cancellationToken);
}
