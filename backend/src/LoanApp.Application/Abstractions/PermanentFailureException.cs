namespace LoanApp.Application.Abstractions;

/// <summary>
/// A failure that retrying cannot fix (e.g. the external service rejected the payload with a 4xx).
/// The event goes straight to the dead-letter queue instead of being retried.
/// </summary>
public sealed class PermanentFailureException(string message, Exception? innerException = null) : Exception(message, innerException);
