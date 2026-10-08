namespace LoanApp.Infrastructure.Outbox;

public sealed class OutboxOptions
{
    public const string Section = "Outbox";

    /// <summary>Turn off to run an instance that only serves HTTP (another instance or a dedicated worker delivers).</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Messages claimed per round. Each belongs to a different ordering key.</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>How long a claim is exclusive. Must exceed BatchSize × HTTP timeout; after a crash, messages are redelivered when it expires.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    public int MaxAttempts { get; set; } = 10;
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Processed messages older than this are deleted. Dead letters are kept until requeued or handled manually.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>The outbox health check reports Degraded when the oldest pending message is older than this.</summary>
    public TimeSpan MaxHealthyBacklogAge { get; set; } = TimeSpan.FromMinutes(5);
}
