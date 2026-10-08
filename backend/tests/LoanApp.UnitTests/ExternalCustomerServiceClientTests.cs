using System.Net;
using LoanApp.Application.Abstractions;
using LoanApp.Application.Events;
using LoanApp.Infrastructure.ExternalService;

namespace LoanApp.UnitTests;

public class ExternalCustomerServiceClientTests
{
    private static readonly CustomerSnapshot Customer = new(
        Guid.NewGuid(), "Ann", "Lee", "222334444", new AddressSnapshot("1 Main St", "Austin", "TX", "78701"), "Acme");
    private static readonly ApplicationSnapshot Application = new(Guid.NewGuid(), Customer.Id, 5_000);

    [Fact]
    public async Task Create_posts_and_update_puts_with_the_idempotency_key()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        var client = Client(handler);
        var key = Guid.NewGuid();

        await client.CreateAsync(Customer, Application, key, default);
        await client.UpdateAsync(Customer, Application, key, default);

        Assert.Equal(["POST /customers", $"PUT /customers/{Customer.Id}"], handler.Requests.Select(r => $"{r.Method} {r.RequestUri!.AbsolutePath}"));
        Assert.All(handler.Requests, r => Assert.Equal(key.ToString(), Assert.Single(r.Headers.GetValues("Idempotency-Key"))));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task Server_errors_and_throttling_are_transient(HttpStatusCode status) =>
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(new StubHandler(status)).CreateAsync(Customer, Application, Guid.NewGuid(), default));

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Client_errors_are_permanent(HttpStatusCode status) =>
        await Assert.ThrowsAsync<PermanentFailureException>(() => Client(new StubHandler(status)).UpdateAsync(Customer, Application, Guid.NewGuid(), default));

    private static ExternalCustomerServiceClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://external/") });

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}") });
        }
    }
}
