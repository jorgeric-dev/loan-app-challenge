using LoanApp.Application.Abstractions;
using LoanApp.Infrastructure.Outbox;
using Microsoft.Extensions.Options;

namespace LoanApp.UnitTests;

public class OutboxRetryPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly OutboxRetryPolicy Policy = new(Options.Create(new OutboxOptions
    {
        MaxAttempts = 5,
        BaseRetryDelay = TimeSpan.FromSeconds(2),
        MaxRetryDelay = TimeSpan.FromSeconds(10),
    }));

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 10)] // 16s capped at the max delay
    public void Backs_off_exponentially_with_jitter(int failedAttempts, double expectedSeconds)
    {
        var next = Policy.NextAttempt(failedAttempts, new HttpRequestException("503"), Now);

        var delay = (next!.Value - Now).TotalSeconds;
        Assert.InRange(delay, expectedSeconds * 0.8, expectedSeconds * 1.2);
    }

    [Fact]
    public void Gives_up_after_max_attempts() =>
        Assert.Null(Policy.NextAttempt(5, new HttpRequestException("503"), Now));

    [Fact]
    public void Permanent_failures_are_not_retried() =>
        Assert.Null(Policy.NextAttempt(1, new PermanentFailureException("400 Bad Request"), Now));
}
