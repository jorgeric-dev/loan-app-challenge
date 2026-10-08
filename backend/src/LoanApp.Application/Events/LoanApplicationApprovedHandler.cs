using LoanApp.Application.Abstractions;

namespace LoanApp.Application.Events;

/// <summary>Background handler: mirrors the customer in the external service (create for new customers, update for returning ones).</summary>
public sealed class LoanApplicationApprovedHandler(IExternalCustomerService externalService)
    : IIntegrationEventHandler<LoanApplicationApproved>
{
    public Task HandleAsync(LoanApplicationApproved approved, CancellationToken cancellationToken) =>
        approved.IsReturningCustomer
            ? externalService.UpdateAsync(approved.Customer, approved.Application, approved.EventId, cancellationToken)
            : externalService.CreateAsync(approved.Customer, approved.Application, approved.EventId, cancellationToken);
}
