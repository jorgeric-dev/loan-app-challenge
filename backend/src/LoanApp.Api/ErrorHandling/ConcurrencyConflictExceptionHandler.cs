using LoanApp.Application.Abstractions;
using Microsoft.AspNetCore.Diagnostics;

namespace LoanApp.Api.ErrorHandling;

/// <summary>Lost a race with a concurrent submission for the same SSN → 409. Nothing was saved; the client may resubmit.</summary>
public sealed class ConcurrencyConflictExceptionHandler(IProblemDetailsService problemDetails, ILogger<ConcurrencyConflictExceptionHandler> logger)
    : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ConcurrencyConflictException)
            return ValueTask.FromResult(false);

        logger.LogWarning(exception, "Concurrent submission conflict");
        return problemDetails.WriteProblemAsync(context, StatusCodes.Status409Conflict, "Concurrent submission",
            "This application was submitted at the same time from somewhere else. Nothing was saved; please submit again.");
    }
}
