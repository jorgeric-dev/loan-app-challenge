using Microsoft.AspNetCore.Mvc;

namespace LoanApp.Api.ErrorHandling;

internal static class ProblemDetailsServiceExtensions
{
    public static async ValueTask<bool> WriteProblemAsync(
        this IProblemDetailsService problemDetails, HttpContext context, int status, string title, string detail)
    {
        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
        });
    }
}
