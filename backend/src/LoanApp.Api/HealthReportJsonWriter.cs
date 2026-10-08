using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LoanApp.Api;

public static class HealthReportJsonWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report) =>
        context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description ?? entry.Value.Exception?.Message,
                    data = entry.Value.Data,
                }),
        });
}
