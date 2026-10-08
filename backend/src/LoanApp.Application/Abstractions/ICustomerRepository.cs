using LoanApp.Domain;
using LoanApp.Domain.Customers;

namespace LoanApp.Application.Abstractions;

public interface ICustomerRepository
{
    /// <summary>Returns the customer (with its application) or null when the SSN is unknown.</summary>
    Task<Customer?> FindBySsnAsync(Ssn ssn, CancellationToken cancellationToken);

    void Add(Customer customer);
}
