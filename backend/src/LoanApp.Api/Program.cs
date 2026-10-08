using System.Text.Json.Serialization;
using LoanApp.Api;
using LoanApp.Api.ErrorHandling;
using LoanApp.Application;
using LoanApp.Infrastructure;
using LoanApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

// `dotnet LoanApp.Api.dll --migrate-only` applies EF Core migrations and exits. Deployments run it once,
// before the new version starts, instead of letting every instance race to migrate on startup.
const string MigrateOnlyFlag = "--migrate-only";
var migrateOnly = args.Contains(MigrateOnlyFlag);

var builder = WebApplication.CreateBuilder(args.Where(arg => arg != MigrateOnlyFlag).ToArray());

builder.AddObservability();
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks().AddInfrastructureHealthChecks();

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// The OpenAPI document reads these options, not the MVC ones: keep enums documented as strings, as they are sent.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<ConcurrencyConflictExceptionHandler>();
builder.Services.AddOpenApi();
// Give in-flight requests and outbox deliveries time to finish (or hand back their lease) on shutdown.
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));

var app = builder.Build();

if (migrateOnly || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<LoanDbContext>().Database.MigrateAsync();
    if (migrateOnly)
        return;
}

app.UseExceptionHandler();
app.MapOpenApi();
if (app.Environment.IsDevelopment())
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Loan API")); // http://localhost:5080/swagger
app.MapControllers();

// Liveness: the process answers. Readiness: it can serve traffic (database reachable).
// /health: everything, including outbox backlog/dead letters (Degraded), as JSON for dashboards and alerts.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthReportJsonWriter.WriteAsync });

app.Run();

public partial class Program;
