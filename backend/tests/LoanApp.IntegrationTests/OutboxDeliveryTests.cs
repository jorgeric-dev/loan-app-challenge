using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LoanApp.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using static LoanApp.IntegrationTests.LoanApi;

namespace LoanApp.IntegrationTests;

/// <summary>Background delivery as it behaves in production: failures, retries, dead letters, several workers, crashes.</summary>
public class OutboxDeliveryTests(LoanAppFactory factory) : IClassFixture<LoanAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Failed_delivery_is_retried_later_instead_of_lost()
    {
        await factory.DeliverPendingEventsAsync(); // start from an empty queue
        var result = await _client.SubmitAsync(Form(NewSsn()));
        factory.ExternalService.FailuresToSimulate = 1;

        await factory.DeliverPendingEventsAsync();

        var message = await factory.LastOutboxMessageAsync();
        Assert.Null(message.ProcessedAt);
        Assert.Equal(1, message.Attempts);
        Assert.True(message.NextAttemptAt > DateTimeOffset.UtcNow);
        Assert.Null(message.LockedUntil);
        Assert.Empty(factory.ExternalService.CallsFor(result.CustomerId));

        await MakeDueAsync(result.CustomerId!.Value);
        await factory.DeliverPendingEventsAsync();

        Assert.Single(factory.ExternalService.CallsFor(result.CustomerId));
    }

    [Fact]
    public async Task Parallel_workers_deliver_each_event_exactly_once_and_in_order_per_customer()
    {
        var customerIds = new List<Guid>();
        for (var i = 0; i < 8; i++)
        {
            var ssn = NewSsn();
            customerIds.Add((await _client.SubmitAsync(Form(ssn, amount: 1_000))).CustomerId!.Value);
            await _client.SubmitAsync(Form(ssn, amount: 2_000)); // returning: an update queued behind the create
        }

        // Four "instances" polling the same table at the same time.
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            for (var idle = 0; idle < 3;)
                idle = await factory.RelayOnceAsync() == 0 ? idle + 1 : 0;
        })));

        var calls = factory.ExternalService.Calls.Where(c => customerIds.Contains(c.Customer.Id)).ToList();
        Assert.Equal(16, calls.Count);
        Assert.Equal(16, calls.Select(c => c.IdempotencyKey).Distinct().Count());
        Assert.All(customerIds, id =>
        {
            var perCustomer = calls.Where(c => c.Customer.Id == id).ToList();
            Assert.Equal(["create", "update"], perCustomer.Select(c => c.Operation));
            Assert.Equal(2_000, perCustomer[^1].Application.RequestedAmount);
        });
    }

    [Fact]
    public async Task A_failing_customer_blocks_only_its_own_later_events()
    {
        await factory.DeliverPendingEventsAsync();
        var ssnA = NewSsn();
        var customerA = (await _client.SubmitAsync(Form(ssnA))).CustomerId!.Value;
        await _client.SubmitAsync(Form(ssnA, amount: 9_000));
        var customerB = (await _client.SubmitAsync(Form(NewSsn()))).CustomerId!.Value;
        factory.ExternalService.Unavailable[customerA] = true;

        await factory.DeliverPendingEventsAsync();

        Assert.Empty(factory.ExternalService.CallsFor(customerA));            // A's create failed, A's update waits behind it
        Assert.Single(factory.ExternalService.CallsFor(customerB));           // B is not stuck behind A

        factory.ExternalService.Unavailable.TryRemove(customerA, out _);
        await MakeDueAsync(customerA);
        await factory.DeliverPendingEventsAsync();

        Assert.Equal(["create", "update"], factory.ExternalService.CallsFor(customerA).Select(c => c.Operation));
    }

    [Fact]
    public async Task Rejected_event_is_dead_lettered_reported_and_can_be_replayed()
    {
        await factory.DeliverPendingEventsAsync();
        var ssn = NewSsn();
        var customerId = (await _client.SubmitAsync(Form(ssn))).CustomerId!.Value;
        await _client.SubmitAsync(Form(ssn, amount: 9_000));
        factory.ExternalService.Rejected[customerId] = true;

        await factory.DeliverPendingEventsAsync();

        // Permanent failure: dead-lettered at the first attempt, and the customer's update is held back.
        var deadLetters = await _client.GetFromJsonAsync<List<DeadLetter>>("/api/operations/events/dead-letters", LoanApi.Json);
        var deadLetter = Assert.Single(deadLetters!, d => d.OrderingKey == customerId.ToString());
        Assert.Equal(1, deadLetter.Attempts);
        Assert.Contains("422", deadLetter.LastError);
        Assert.Empty(factory.ExternalService.CallsFor(customerId));
        var health = await _client.GetFromJsonAsync<JsonElement>("/health");
        Assert.Equal("Degraded", health.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health/ready")).StatusCode); // still takes traffic

        // Operator fixes the cause and replays.
        factory.ExternalService.Rejected.TryRemove(customerId, out _);
        var requeue = await _client.PostAsync($"/api/operations/events/dead-letters/{deadLetter.Id}/requeue", null);
        Assert.Equal(HttpStatusCode.NoContent, requeue.StatusCode);
        await factory.DeliverPendingEventsAsync();

        Assert.Equal(["create", "update"], factory.ExternalService.CallsFor(customerId).Select(c => c.Operation));
        var status = await _client.GetFromJsonAsync<EventDeliveryStatus>("/api/operations/events/status", LoanApi.Json);
        Assert.Equal(0, status!.DeadLettered);
    }

    [Fact]
    public async Task Lease_of_a_crashed_worker_expires_and_the_event_is_redelivered()
    {
        await factory.DeliverPendingEventsAsync();
        var customerId = (await _client.SubmitAsync(Form(NewSsn()))).CustomerId!.Value;
        var key = customerId.ToString();

        // A worker claimed the message and died: while its lease is valid nobody else takes it…
        await factory.QueryAsync(db => db.OutboxMessages.Where(m => m.OrderingKey == key).ExecuteUpdateAsync(set => set
            .SetProperty(m => m.LockedBy, "crashed-worker")
            .SetProperty(m => m.LockedUntil, DateTimeOffset.UtcNow.AddMinutes(5))));
        await factory.DeliverPendingEventsAsync();
        Assert.Empty(factory.ExternalService.CallsFor(customerId));

        // …and once it expires, another worker delivers it.
        await factory.QueryAsync(db => db.OutboxMessages.Where(m => m.OrderingKey == key).ExecuteUpdateAsync(set => set
            .SetProperty(m => m.LockedUntil, DateTimeOffset.UtcNow.AddSeconds(-1))));
        await factory.DeliverPendingEventsAsync();
        Assert.Single(factory.ExternalService.CallsFor(customerId));
    }

    private Task<int> MakeDueAsync(Guid customerId)
    {
        var key = customerId.ToString();
        return factory.QueryAsync(db => db.OutboxMessages.Where(m => m.OrderingKey == key && m.ProcessedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.NextAttemptAt, DateTimeOffset.UtcNow)));
    }
}
