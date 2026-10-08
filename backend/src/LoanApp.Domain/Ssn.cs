namespace LoanApp.Domain;

/// <summary>US Social Security Number, normalized to 9 digits (dashes and spaces are ignored).</summary>
public sealed record Ssn
{
    public string Value { get; }

    private Ssn(string value) => Value = value;

    public static Ssn Parse(string input)
    {
        var digits = new string((input ?? string.Empty).Where(c => c != '-' && c != ' ').ToArray());
        if (digits.Length != 9 || !digits.All(char.IsAsciiDigit))
            throw new DomainException("SSN must contain exactly 9 digits.");
        return new Ssn(digits);
    }

    public override string ToString() => $"***-**-{Value[^4..]}";
}
