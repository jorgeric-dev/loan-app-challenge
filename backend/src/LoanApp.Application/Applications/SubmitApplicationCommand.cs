using LoanApp.Domain;
using LoanApp.Domain.Customers;
using LoanApp.Domain.Decisions;

namespace LoanApp.Application.Applications;

/// <summary>The application form, as submitted.</summary>
public sealed record SubmitApplicationCommand(
    string FirstName,
    string LastName,
    string Street,
    string City,
    string State,
    string ZipCode,
    string CompanyName,
    decimal RequestedAmount,
    string Ssn)
{
    /// <exception cref="DomainException">When the form data is invalid.</exception>
    public LoanRequest ToLoanRequest() => new(
        new Applicant(
            Domain.Ssn.Parse(Ssn),
            FirstName,
            LastName,
            new Address(Street, City, State, ZipCode),
            CompanyName),
        RequestedAmount);
}
