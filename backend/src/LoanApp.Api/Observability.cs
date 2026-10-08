using LoanApp.Application.Diagnostics;
using LoanApp.Infrastructure.Outbox;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace LoanApp.Api;

/// <summary>
/// Traces, metrics and logs through OpenTelemetry. Exported over OTLP only when OTEL_EXPORTER_OTLP_ENDPOINT is set
/// (docker compose points it at the Aspire dashboard; in production, at any OTLP collector — Grafana, Datadog, Azure Monitor…).
/// </summary>
public static class Observability
{
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        var exportOtlp = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = true;
        });

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(builder.Configuration["OTEL_SERVICE_NAME"] ?? "loan-api"))
            .WithTracing(tracing => tracing
                .SetSampler(new ParentBasedSampler(new DropOutboxPollingSampler()))
                .AddSource(LoanAppTelemetry.Name)
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddMeter(LoanAppTelemetry.Name)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        if (exportOtlp)
            telemetry.UseOtlpExporter();
        return builder;
    }

    /// <summary>
    /// The outbox polls every second on every instance; recording those idle rounds would bury real traces.
    /// Delivering an event is still traced, as part of the trace of the request that published it.
    /// </summary>
    private sealed class DropOutboxPollingSampler : Sampler
    {
        public override SamplingResult ShouldSample(in SamplingParameters parameters) =>
            new(parameters.Name == OutboxProcessor.PollActivityName ? SamplingDecision.Drop : SamplingDecision.RecordAndSample);
    }
}
