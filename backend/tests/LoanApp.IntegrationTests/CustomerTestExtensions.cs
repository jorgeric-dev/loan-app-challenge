using LoanApp.Domain.Customers;

namespace LoanApp.IntegrationTests;

internal static class CustomerTestExtensions
{
    public static Applicant ToApplicantWith(this Customer customer, string firstName) =>
        new(customer.Ssn, firstName, customer.LastName, customer.Address, customer.CompanyName);
}
