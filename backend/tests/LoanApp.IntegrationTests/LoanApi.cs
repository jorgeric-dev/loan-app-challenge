using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LoanApp.Application.Applications;

namespace LoanApp.IntegrationTests;

/// <summary>Test helpers to call the API like the frontend does.</summary>
internal static class LoanApi
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static async Task<SubmitApplicationResult> SubmitAsync(this HttpClient client, Dictionary<string, object> form)
    {
        var response = await client.PostAsJsonAsync("/api/applications", form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SubmitApplicationResult>(Json))!;
    }

    public static Dictionary<string, object> Form(string ssn, string state = "TX", string firstName = "Ann", decimal amount = 5_000) => new()
    {
        ["firstName"] = firstName,
        ["lastName"] = "Lee",
        ["street"] = "1 Main St",
        ["city"] = "Austin",
        ["state"] = state,
        ["zipCode"] = "78701",
        ["companyName"] = "Acme",
        ["requestedAmount"] = amount,
        ["ssn"] = ssn,
    };

    public static string NewSsn() => Random.Shared.Next(200_000_000, 899_999_999).ToString();
}
