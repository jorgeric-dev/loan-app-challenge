using LoanApp.Application.Diagnostics;
using LoanApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LoanApp.Infrastructure.Outbox;

/// <summary>
/// Hosting only: wakes up every polling interval, drains due messages through <see cref="OutboxRelay"/>,
/// and periodically purges old processed messages. Delivery logic lives in the relay.
/// </summary>
public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    TimeProvider time,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    /// <summary>Wraps each polling round. Samplers can drop it (and the polling SQL under it) to keep traces free of noise.</summary>
    public const string PollActivityName = "outbox poll";

    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Outbox delivery is disabled on this instance");
            return;
        }

        var nextCleanup = time.GetUtcNow();
        using var timer = new PeriodicTimer(settings.PollingInterval, time);
        do
        {
            using var poll = LoanAppTelemetry.ActivitySource.StartActivity(PollActivityName);
            try
            {
                while (!stoppingToken.IsCancellationRequested && await RelayOnceAsync(stoppingToken) > 0) { }

                if (time.GetUtcNow() >= nextCleanup)
                {
                    await PurgeProcessedAsync(settings.Retention, stoppingToken);
                    nextCleanup = time.GetUtcNow() + CleanupInterval;
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // e.g. database unreachable: keep the worker alive and try again on the next tick.
                logger.LogError(ex, "Outbox polling failed; retrying on next tick");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<int> RelayOnceAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OutboxRelay>().RelayBatchAsync(stoppingToken);
    }

    private async Task PurgeProcessedAsync(TimeSpan retention, CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var cutoff = time.GetUtcNow() - retention;
        var purged = await scope.ServiceProvider.GetRequiredService<LoanDbContext>().OutboxMessages
            .Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(stoppingToken);
        if (purged > 0)
            logger.LogInformation("Purged {Count} processed outbox messages older than {Cutoff:O}", purged, cutoff);
    }
}
