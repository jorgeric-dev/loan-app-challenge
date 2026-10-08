namespace LoanApp.Application.Abstractions;

/// <summary>
/// The commit lost a race with a concurrent request (same SSN submitted twice at the same time).
/// Nothing was saved; submitting again is safe.
/// </summary>
public sealed class ConcurrencyConflictException(string message, Exception innerException) : Exception(message, innerException);
