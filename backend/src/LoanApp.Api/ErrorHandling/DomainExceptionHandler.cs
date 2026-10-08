using LoanApp.Domain;
using Microsoft.AspNetCore.Diagnostics;

namespace LoanApp.Api.ErrorHandling;

/// <summary>Domain validation errors (bad SSN, missing state, …) → 400.</summary>
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken) =>
        exception is DomainException
            ? problemDetails.WriteProblemAsync(context, StatusCodes.Status400BadRequest, "Invalid application", exception.Message)
            : ValueTask.FromResult(false);
}
