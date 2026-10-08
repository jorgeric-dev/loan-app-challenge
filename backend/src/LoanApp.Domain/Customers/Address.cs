namespace LoanApp.Domain.Customers;

public sealed record Address
{
    public string Street { get; }
    public string City { get; }
    /// <summary>Two-letter US state code, upper case (e.g. "NY").</summary>
    public string State { get; }
    public string ZipCode { get; }

    public Address(string street, string city, string state, string zipCode)
    {
        Street = Required(street, nameof(Street));
        City = Required(city, nameof(City));
        ZipCode = Required(zipCode, nameof(ZipCode));
        State = Required(state, nameof(State)).ToUpperInvariant();
        if (State.Length != 2 || !State.All(char.IsAsciiLetter))
            throw new DomainException("State must be a two-letter code.");
    }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new DomainException($"{name} is required.") : value.Trim();
}
