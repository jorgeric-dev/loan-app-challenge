namespace LoanApp.Domain.Customers;

/// <summary>Personal data typed in the application form.</summary>
public sealed record Applicant
{
    public Ssn Ssn { get; }
    public string FirstName { get; }
    public string LastName { get; }
    public Address Address { get; }
    public string CompanyName { get; }

    public Applicant(Ssn ssn, string firstName, string lastName, Address address, string companyName)
    {
        Ssn = ssn;
        FirstName = Required(firstName, nameof(FirstName));
        LastName = Required(lastName, nameof(LastName));
        Address = address;
        CompanyName = Required(companyName, nameof(CompanyName));
    }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new DomainException($"{name} is required.") : value.Trim();
}
