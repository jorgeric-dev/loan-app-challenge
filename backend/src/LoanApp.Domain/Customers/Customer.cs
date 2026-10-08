namespace LoanApp.Domain.Customers;

/// <summary>
/// Aggregate root. A customer is identified by SSN and owns exactly one loan application,
/// which is updated (not duplicated) when the customer applies again.
/// </summary>
public sealed class Customer
{
    public Guid Id { get; private set; }
    public Ssn Ssn { get; private set; } = null!;
    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public Address Address { get; private set; } = null!;
    public string CompanyName { get; private set; } = null!;
    public LoanApplication Application { get; private set; } = null!;

    private Customer() { } // EF Core

    public static Customer Create(Applicant applicant, decimal requestedAmount)
    {
        var customer = new Customer { Id = Guid.NewGuid(), Ssn = applicant.Ssn };
        customer.SetDetails(applicant);
        customer.Application = new LoanApplication(customer.Id, requestedAmount);
        return customer;
    }

    /// <summary>Returning customer: overwrite personal data and the existing application with the latest submission.</summary>
    public void Resubmit(Applicant applicant, decimal requestedAmount)
    {
        if (applicant.Ssn != Ssn)
            throw new DomainException("A resubmission must belong to the same SSN.");
        SetDetails(applicant);
        Application.ChangeRequestedAmount(requestedAmount);
    }

    private void SetDetails(Applicant applicant)
    {
        FirstName = applicant.FirstName;
        LastName = applicant.LastName;
        Address = applicant.Address;
        CompanyName = applicant.CompanyName;
    }
}
