using System.Net;
using System.Net.Http.Json;
using LoanApp.Application.Abstractions;
using LoanApp.Application.Events;

namespace LoanApp.Infrastructure.ExternalService;

public sealed class ExternalCustomerServiceOptions
{
    public const string Section = "ExternalCustomerService";
    public Uri BaseUrl { get; set; } = null!;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// HTTP client for the external customer service.
/// Create = POST /customers, update = PUT /customers/{customerId}, both with the full state and an
/// <c>Idempotency-Key</c> header (the event id), so a redelivered event is never applied twice.
/// 408, 429 and 5xx are transient (retried by the outbox); any other 4xx is permanent (dead-lettered at once).
/// </summary>
public sealed class ExternalCustomerServiceClient(HttpClient http) : IExternalCustomerService
{
    public Task CreateAsync(CustomerSnapshot customer, ApplicationSnapshot application, Guid idempotencyKey, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, "customers", customer, application, idempotencyKey, cancellationToken);

    public Task UpdateAsync(CustomerSnapshot customer, ApplicationSnapshot application, Guid idempotencyKey, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Put, $"customers/{customer.Id}", customer, application, idempotencyKey, cancellationToken);

    private async Task SendAsync(
        HttpMethod method, string path, CustomerSnapshot customer, ApplicationSnapshot application, Guid idempotencyKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(new CustomerPayload(customer, application)),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey.ToString());

        using var response = await http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var error = $"{method} /{path} returned {(int)response.StatusCode}: {body}";
        if (IsTransient(response.StatusCode))
            throw new HttpRequestException(error, null, response.StatusCode);
        throw new PermanentFailureException(error);
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;

    private sealed record CustomerPayload(CustomerSnapshot Customer, ApplicationSnapshot Application);
}
