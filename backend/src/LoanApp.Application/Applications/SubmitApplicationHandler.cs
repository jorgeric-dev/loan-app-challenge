using LoanApp.Application.Abstractions;
using LoanApp.Application.Diagnostics;
using LoanApp.Application.Events;
using LoanApp.Domain.Customers;
using LoanApp.Domain.Decisions;
using Microsoft.Extensions.Logging;

namespace LoanApp.Application.Applications;

/// <summary>
/// Use case: decide on a loan application and, when approved, create or update the customer and its
/// application and publish <see cref="LoanApplicationApproved"/> — all in a single unit of work.
/// </summary>
public sealed class SubmitApplicationHandler(
    DecisionEngine decisionEngine,
    ICustomerRepository customers,
    IEventPublisher events,
    IUnitOfWork unitOfWork,
    ApplicationMetrics metrics,
    ILogger<SubmitApplicationHandler> logger)
{
    public async Task<SubmitApplicationResult> HandleAsync(SubmitApplicationCommand command, CancellationToken cancellationToken)
    {
        var request = command.ToLoanRequest();

        var decision = decisionEngine.Decide(request);
        if (!decision.IsApproved)
        {
            var reasonCodes = decision.DenialReasons.Select(reason => reason.Code).ToList();
            metrics.Denied(reasonCodes);
            logger.LogInformation("Application for SSN {Ssn} denied: {Reasons}", request.Applicant.Ssn, reasonCodes);
            return SubmitApplicationResult.Denied(reasonCodes);
        }

        var customer = await customers.FindBySsnAsync(request.Applicant.Ssn, cancellationToken);
        var isReturningCustomer = customer is not null;
        if (customer is null)
        {
            customer = Customer.Create(request.Applicant, request.RequestedAmount);
            customers.Add(customer);
        }
        else
        {
            customer.Resubmit(request.Applicant, request.RequestedAmount);
        }

        events.Publish(LoanApplicationApproved.From(customer, isReturningCustomer));
        await unitOfWork.CommitAsync(cancellationToken);

        metrics.Approved(isReturningCustomer);
        logger.LogInformation("Application {ApplicationId} approved for customer {CustomerId} (returning: {IsReturningCustomer})",
            customer.Application.Id, customer.Id, isReturningCustomer);
        return SubmitApplicationResult.Approved(customer.Id, customer.Application.Id, isReturningCustomer);
    }
}
