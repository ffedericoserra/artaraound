using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ArtAround.ContractTests;

/// <summary>Shared HttpClient and request helpers for test classes and fixtures.</summary>
internal static class Api
{
    // One client for the whole run: HttpClient is thread-safe and meant to be reused.
    // Never set DefaultRequestHeaders on it: test classes run in parallel and would overwrite each other.
    public static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var baseUri = Environment.GetEnvironmentVariable("API_BASE_URI") ?? "http://localhost";

        // Fall back only when unset: a typo like "80OO" must fail, not silently hit port 8000.
        var portValue = Environment.GetEnvironmentVariable("API_PORT");
        var port = 8000;
        if (portValue is not null && !int.TryParse(portValue, out port))
        {
            throw new InvalidOperationException($"API_PORT is not a valid port: '{portValue}'.");
        }

        return new HttpClient { BaseAddress = new UriBuilder(baseUri) { Port = port }.Uri };
    }

    /// <summary>Sends one request, with a bearer token and a JSON body when given. The caller disposes the response.</summary>
    public static async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, string? token = null, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await Client.SendAsync(request);
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    public static Task<HttpResponseMessage> AttemptRegisterAsync(string username, string password) =>
        SendAsync(HttpMethod.Post, TestData.RegisterApi,
            body: new { username, password, email = $"{username}@test.local" });

    /// <summary>Registers a fresh user and returns its JWT (register already logs in).</summary>
    public static async Task<string> RegisterAndGetTokenAsync(string username)
    {
        using var response = await AttemptRegisterAsync(username, TestData.TestPassword);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await ReadJsonAsync(response);
        return body.GetProperty("token").GetString()!;
    }
}
