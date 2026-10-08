using LoanApp.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace LoanApp.Infrastructure.Outbox;

/// <summary>
/// Decides what happens after a failed delivery: retry later (exponential backoff with ±20% jitter, so many
/// instances do not retry in lock-step against a recovering service) or give up (dead letter).
/// </summary>
public sealed class OutboxRetryPolicy(IOptions<OutboxOptions> options)
{
    /// <returns>When to retry, or null to dead-letter the message.</returns>
    public DateTimeOffset? NextAttempt(int failedAttempts, Exception error, DateTimeOffset now)
    {
        var settings = options.Value;
        if (error is PermanentFailureException || failedAttempts >= settings.MaxAttempts)
            return null;

        var exponential = settings.BaseRetryDelay.TotalMilliseconds * Math.Pow(2, failedAttempts - 1);
        var capped = Math.Min(exponential, settings.MaxRetryDelay.TotalMilliseconds);
        var jitter = 0.8 + Random.Shared.NextDouble() * 0.4;
        return now + TimeSpan.FromMilliseconds(capped * jitter);
    }
}
