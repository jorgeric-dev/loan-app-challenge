using LoanApp.Application.Abstractions;
using LoanApp.Domain.Decisions;
using LoanApp.Infrastructure.Decisions;
using LoanApp.Infrastructure.ExternalService;
using LoanApp.Infrastructure.Outbox;
using LoanApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using ApplicationLayer = LoanApp.Application.DependencyInjection;

namespace LoanApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddPersistence(configuration);
        services.AddOutbox(configuration);
        services.AddExternalCustomerService(configuration);

        services.Configure<SsnBlacklistOptions>(options =>
            options.Ssns = configuration.GetSection(SsnBlacklistOptions.Section).Get<string[]>() ?? []);
        services.AddSingleton<ISsnBlacklist, ConfiguredSsnBlacklist>();
        return services;
    }

    /// <summary>"ready" checks gate traffic (database); the outbox check only reports Degraded, for alerting.</summary>
    public static IHealthChecksBuilder AddInfrastructureHealthChecks(this IHealthChecksBuilder builder) =>
        builder
            .AddDbContextCheck<LoanDbContext>("database", tags: ["ready"])
            .AddCheck<OutboxHealthCheck>("outbox");

    private static void AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<LoanDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("LoanDb"),
                sql => sql.EnableRetryOnFailure(maxRetryCount: 5))); // transient faults: failover, network blips, throttling
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<LoanDbContext>());
        services.AddScoped<ICustomerRepository, CustomerRepository>();
    }

    private static void AddOutbox(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.Section));
        services.AddScoped<IEventPublisher, OutboxEventPublisher>();
        services.AddScoped<IEventDeliveryOperations, EventDeliveryOperations>();
        services.AddScoped<OutboxRelay>();
        services.AddSingleton<OutboxRetryPolicy>();
        services.AddSingleton<OutboxMetrics>();
        services.AddSingleton<IntegrationEventDispatcher>();
        services.AddHostedService<OutboxProcessor>();

        // One route per event handled in the application layer (discovered, not listed by hand).
        var events = ApplicationLayer.ConcreteTypes(typeof(ApplicationLayer).Assembly)
            .SelectMany(ApplicationLayer.IntegrationEventHandlerContracts)
            .Select(contract => contract.GetGenericArguments()[0])
            .Distinct();
        foreach (var eventType in events)
            services.AddSingleton(typeof(IOutboxRoute), typeof(OutboxRoute<>).MakeGenericType(eventType));
    }

    private static void AddExternalCustomerService(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExternalCustomerServiceOptions>(configuration.GetSection(ExternalCustomerServiceOptions.Section));
        services.AddHttpClient<IExternalCustomerService, ExternalCustomerServiceClient>((sp, http) =>
                http.BaseAddress = sp.GetRequiredService<IOptions<ExternalCustomerServiceOptions>>().Value.BaseUrl)
            .AddResilienceHandler("external-customer-service", (pipeline, context) =>
            {
                // No retries here: the outbox already retries durably. The breaker stops hammering a service that is down
                // (calls fail fast and are rescheduled), the timeout bounds each call.
                var settings = context.ServiceProvider.GetRequiredService<IOptions<ExternalCustomerServiceOptions>>().Value;
                pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    MinimumThroughput = 5,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = TimeSpan.FromSeconds(30),
                });
                pipeline.AddTimeout(settings.Timeout);
            });
    }
}
