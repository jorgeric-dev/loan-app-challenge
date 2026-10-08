using System.Diagnostics;

namespace LoanApp.Application.Diagnostics;

/// <summary>
/// Names of the app's own traces and metrics. Only BCL types (System.Diagnostics) are used here;
/// exporting them (OpenTelemetry/OTLP) is configured in the API, so this layer stays vendor-free.
/// </summary>
public static class LoanAppTelemetry
{
    public const string Name = "LoanApp";

    public static readonly ActivitySource ActivitySource = new(Name);
}
