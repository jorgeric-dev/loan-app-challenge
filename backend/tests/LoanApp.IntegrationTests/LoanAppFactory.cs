using System.Collections.Concurrent;
using LoanApp.Application.Abstractions;
using LoanApp.Application.Events;
using LoanApp.Infrastructure.Outbox;
using LoanApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace LoanApp.IntegrationTests;

/// <summary>
/// Runs the real API against a real SQL Server (the "LoanDb" connection string, default: the docker-compose one),
/// on a throw-away database. The external service is faked and the outbox is drained on demand by the tests.
/// </summary>
public sealed class LoanAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public FakeExternalCustomerService ExternalService { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var connectionString = new SqlConnectionStringBuilder(config.Build().GetConnectionString("LoanDb"))
            {
                InitialCatalog = $"LoanAppTests_{Guid.NewGuid():N}",
            };
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:LoanDb"] = connectionString.ConnectionString,
                ["Database:MigrateOnStartup"] = "true",
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IExternalCustomerService>();
            services.AddSingleton<IExternalCustomerService>(ExternalService);
        });
    }

    /// <summary>Delivers every due outbox message, like one background worker would.</summary>
    public async Task DeliverPendingEventsAsync()
    {
        while (await RelayOnceAsync() > 0) { }
    }

    /// <summary>One worker round: claim a batch and deliver it.</summary>
    public async Task<int> RelayOnceAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OutboxRelay>().RelayBatchAsync(CancellationToken.None);
    }

    public async Task<T> QueryAsync<T>(Func<LoanDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<LoanDbContext>());
    }

    public Task<OutboxMessage> LastOutboxMessageAsync() =>
        QueryAsync(db => db.OutboxMessages.AsNoTracking().OrderBy(m => m.Id).LastAsync());

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        await QueryAsync(db => db.Database.EnsureDeletedAsync());
        await DisposeAsync();
    }
}

public sealed record ExternalCall(string Operation, CustomerSnapshot Customer, ApplicationSnapshot Application, Guid IdempotencyKey);

/// <summary>Thread-safe fake that records calls and can simulate outages per customer.</summary>
public sealed class FakeExternalCustomerService : IExternalCustomerService
{
    private readonly ConcurrentQueue<ExternalCall> _calls = new();
    private int _failuresToSimulate;

    public IReadOnlyList<ExternalCall> Calls => _calls.ToList();

    /// <summary>Customers whose calls fail with a transient error (service down for them).</summary>
    public ConcurrentDictionary<Guid, bool> Unavailable { get; } = new();

    /// <summary>Customers whose calls are rejected permanently (e.g. 422).</summary>
    public ConcurrentDictionary<Guid, bool> Rejected { get; } = new();

    public int FailuresToSimulate
    {
        set => Interlocked.Exchange(ref _failuresToSimulate, value);
    }

    public IReadOnlyList<ExternalCall> CallsFor(Guid? customerId) => Calls.Where(c => c.Customer.Id == customerId).ToList();

    public Task CreateAsync(CustomerSnapshot customer, ApplicationSnapshot application, Guid idempotencyKey, CancellationToken cancellationToken) =>
        Record(new ExternalCall("create", customer, application, idempotencyKey));

    public Task UpdateAsync(CustomerSnapshot customer, ApplicationSnapshot application, Guid idempotencyKey, CancellationToken cancellationToken) =>
        Record(new ExternalCall("update", customer, application, idempotencyKey));

    private Task Record(ExternalCall call)
    {
        if (Rejected.ContainsKey(call.Customer.Id))
            throw new PermanentFailureException("422 Unprocessable Entity");
        if (Unavailable.ContainsKey(call.Customer.Id) || Interlocked.Decrement(ref _failuresToSimulate) >= 0)
            throw new HttpRequestException("Simulated outage");
        _calls.Enqueue(call);
        return Task.CompletedTask;
    }
}
