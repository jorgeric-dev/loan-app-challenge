namespace LoanApp.Domain;

/// <summary>Raised when a domain invariant is violated.</summary>
public sealed class DomainException(string message) : Exception(message);
