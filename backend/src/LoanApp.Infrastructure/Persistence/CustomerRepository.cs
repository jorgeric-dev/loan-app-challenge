using LoanApp.Application.Abstractions;
using LoanApp.Domain;
using LoanApp.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace LoanApp.Infrastructure.Persistence;

public sealed class CustomerRepository(LoanDbContext db) : ICustomerRepository
{
    public Task<Customer?> FindBySsnAsync(Ssn ssn, CancellationToken cancellationToken) =>
        db.Customers.Include(c => c.Application).SingleOrDefaultAsync(c => c.Ssn == ssn, cancellationToken);

    public void Add(Customer customer) => db.Customers.Add(customer);
}
