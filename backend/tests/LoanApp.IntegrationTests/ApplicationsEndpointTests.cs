using System.Net;
using System.Net.Http.Json;
using LoanApp.Application.Abstractions;
using LoanApp.Application.Applications;
using LoanApp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static LoanApp.IntegrationTests.LoanApi;

namespace LoanApp.IntegrationTests;

public class ApplicationsEndpointTests(LoanAppFactory factory) : IClassFixture<LoanAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Approved_new_customer_is_saved_and_created_in_the_external_service()
    {
        var ssn = NewSsn();

        var result = await _client.SubmitAsync(Form(ssn));

        Assert.Equal(ApplicationStatus.Approved, result.Status);
        Assert.False(result.IsReturningCustomer);
        var customer = await factory.QueryAsync(db => db.Customers.Include(c => c.Application).SingleAsync(c => c.Ssn == Ssn.Parse(ssn)));
        Assert.Equal(result.CustomerId, customer.Id);
        Assert.Equal(result.ApplicationId, customer.Application.Id);

        await factory.DeliverPendingEventsAsync();

        var call = Assert.Single(factory.ExternalService.CallsFor(customer.Id));
        Assert.Equal("create", call.Operation);
        Assert.Equal(5_000, call.Application.RequestedAmount);
    }

    [Fact]
    public async Task Returning_customer_updates_the_existing_records_and_the_external_service()
    {
        var ssn = NewSsn();
        var first = await _client.SubmitAsync(Form(ssn, firstName: "Ann", amount: 5_000));
        await factory.DeliverPendingEventsAsync();

        var second = await _client.SubmitAsync(Form(ssn, firstName: "Annie", amount: 12_500));

        Assert.True(second.IsReturningCustomer);
        Assert.Equal(first.CustomerId, second.CustomerId);
        Assert.Equal(first.ApplicationId, second.ApplicationId);
        var (customers, applications) = await factory.QueryAsync(async db => (
            await db.Customers.Include(c => c.Application).Where(c => c.Ssn == Ssn.Parse(ssn)).ToListAsync(),
            await db.LoanApplications.CountAsync(a => a.CustomerId == first.CustomerId)));
        var customer = Assert.Single(customers);
        Assert.Equal(1, applications);
        Assert.Equal("Annie", customer.FirstName);
        Assert.Equal(12_500, customer.Application.RequestedAmount);

        await factory.DeliverPendingEventsAsync();

        var calls = factory.ExternalService.CallsFor(customer.Id);
        Assert.Equal(["create", "update"], calls.Select(c => c.Operation));
        Assert.Equal(12_500, calls[^1].Application.RequestedAmount);
        Assert.NotEqual(calls[0].IdempotencyKey, calls[1].IdempotencyKey);
    }

    [Theory]
    [InlineData("NY", null, "STATE_NOT_SERVED")]
    [InlineData("TX", "123-45-6789", "SSN_BLACKLISTED")]
    public async Task Denied_application_saves_nothing_and_publishes_nothing(string state, string? ssn, string expectedReason)
    {
        ssn ??= NewSsn();
        var outboxBefore = await OutboxCountAsync();

        var result = await _client.SubmitAsync(Form(ssn, state: state));

        Assert.Equal(ApplicationStatus.Denied, result.Status);
        Assert.Equal([expectedReason], result.DenialReasons);
        Assert.False(await factory.QueryAsync(db => db.Customers.AnyAsync(c => c.Ssn == Ssn.Parse(ssn))));
        Assert.Equal(outboxBefore, await OutboxCountAsync());
    }

    [Theory]
    [InlineData("ssn", "12-34")]
    [InlineData("state", "")]
    [InlineData("requestedAmount", 0)]
    public async Task Invalid_input_returns_400(string field, object value)
    {
        var form = Form(NewSsn());
        form[field] = value;

        var response = await _client.PostAsJsonAsync("/api/applications", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Failure_while_saving_rolls_back_customer_application_and_event()
    {
        var ssn = NewSsn();
        await _client.SubmitAsync(Form(ssn, amount: 5_000));
        var outboxBefore = await OutboxCountAsync();

        // Make the outbox insert (the last statement of the transaction) fail inside SQL Server.
        await factory.QueryAsync(db => db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER FailOutbox ON OutboxMessages AFTER INSERT AS THROW 50000, 'outbox unavailable', 1;"));
        try
        {
            var newCustomer = await _client.PostAsJsonAsync("/api/applications", Form(NewSsn()));
            var returningCustomer = await _client.PostAsJsonAsync("/api/applications", Form(ssn, firstName: "Changed", amount: 99_000));

            Assert.Equal(HttpStatusCode.InternalServerError, newCustomer.StatusCode);
            Assert.Equal(HttpStatusCode.InternalServerError, returningCustomer.StatusCode);
        }
        finally
        {
            await factory.QueryAsync(db => db.Database.ExecuteSqlRawAsync("DROP TRIGGER FailOutbox;"));
        }

        var customer = await factory.QueryAsync(db => db.Customers.Include(c => c.Application).SingleAsync(c => c.Ssn == Ssn.Parse(ssn)));
        Assert.Equal("Ann", customer.FirstName);
        Assert.Equal(5_000, customer.Application.RequestedAmount);
        Assert.Equal(outboxBefore, await OutboxCountAsync());
        Assert.Equal(0, await factory.QueryAsync(db => db.LoanApplications.CountAsync(a => !db.Customers.Any(c => c.Id == a.CustomerId))));
    }

    [Fact]
    public async Task Concurrent_submissions_for_one_ssn_never_create_two_customers()
    {
        var ssn = NewSsn();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            _client.PostAsJsonAsync("/api/applications", Form(ssn, amount: 1_000 + i))));

        // Each request either wins (200) or loses the race cleanly (409, nothing saved) — never a 500 or a duplicate.
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        Assert.Equal(1, await factory.QueryAsync(db => db.Customers.CountAsync(c => c.Ssn == Ssn.Parse(ssn))));
        var approved = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var events = await factory.QueryAsync(db => db.OutboxMessages.CountAsync(m => m.Payload.Contains(ssn)));
        Assert.Equal(approved, events); // one event per committed submission, none for the rejected ones
    }

    [Fact]
    public async Task Stale_update_of_a_customer_is_rejected_as_a_concurrency_conflict()
    {
        var ssn = NewSsn();
        await _client.SubmitAsync(Form(ssn));
        await using var first = factory.Services.CreateAsyncScope();
        await using var second = factory.Services.CreateAsyncScope();
        var firstCopy = await first.ServiceProvider.GetRequiredService<ICustomerRepository>().FindBySsnAsync(Ssn.Parse(ssn), default);
        var secondCopy = await second.ServiceProvider.GetRequiredService<ICustomerRepository>().FindBySsnAsync(Ssn.Parse(ssn), default);

        firstCopy!.Resubmit(firstCopy.ToApplicantWith(firstName: "First"), 7_000);
        await first.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(default);
        secondCopy!.Resubmit(secondCopy.ToApplicantWith(firstName: "Second"), 8_000);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            second.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(default));
    }

    [Fact]
    public async Task Published_event_carries_the_request_trace_context()
    {
        await _client.SubmitAsync(Form(NewSsn()));

        var message = await factory.LastOutboxMessageAsync();

        Assert.Matches("^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$", message.TraceParent);
    }

    private Task<int> OutboxCountAsync() =>
        factory.QueryAsync(db => db.OutboxMessages.CountAsync());
}
