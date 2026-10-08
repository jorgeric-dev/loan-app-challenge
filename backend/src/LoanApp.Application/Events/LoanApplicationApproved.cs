using LoanApp.Application.Abstractions;
using LoanApp.Domain.Customers;

namespace LoanApp.Application.Events;

/// <summary>
/// Integration event raised when an application is approved and saved.
/// Carries a snapshot of the data as it was committed, so the background handler needs no database access.
/// </summary>
public sealed record LoanApplicationApproved(
    Guid EventId,
    bool IsReturningCustomer,
    CustomerSnapshot Customer,
    ApplicationSnapshot Application) : IIntegrationEvent
{
    // Explicit implementation: used for ordering, not part of the serialized payload.
    string IIntegrationEvent.OrderingKey => Customer.Id.ToString();

    public static LoanApplicationApproved From(Customer customer, bool isReturningCustomer) => new(
        Guid.NewGuid(),
        isReturningCustomer,
        new CustomerSnapshot(
            customer.Id,
            customer.FirstName,
            customer.LastName,
            customer.Ssn.Value,
            new AddressSnapshot(customer.Address.Street, customer.Address.City, customer.Address.State, customer.Address.ZipCode),
            customer.CompanyName),
        new ApplicationSnapshot(customer.Application.Id, customer.Id, customer.Application.RequestedAmount));
}

public sealed record CustomerSnapshot(Guid Id, string FirstName, string LastName, string Ssn, AddressSnapshot Address, string CompanyName);

public sealed record AddressSnapshot(string Street, string City, string State, string ZipCode);

public sealed record ApplicationSnapshot(Guid Id, Guid CustomerId, decimal RequestedAmount);
