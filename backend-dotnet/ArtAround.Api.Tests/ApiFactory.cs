using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArtAround.Api.Data;
using ArtAround.Api.Seeding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace ArtAround.Api.Tests;

/// <summary>
/// The real API in memory (WebApplicationFactory) over a real, throwaway Postgres
/// (Testcontainers). Row locks, xmin and constraints are what these tests are about,
/// so an in-memory database would prove nothing. One container per test run: tests
/// stay independent by registering their own users.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // The image is configurable so the tests can reuse whatever Postgres is already pulled.
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder(Environment.GetEnvironmentVariable("TEST_POSTGRES_IMAGE") ?? "postgres:17").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<Seeder>().SeedAsync(CancellationToken.None);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    /* ---------- helpers ---------- */

    public async Task<string> RegisterAsync()
    {
        var username = "it_" + Guid.NewGuid().ToString("N")[..12];
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/register",
            new { username, email = $"{username}@test.local", password = "12345678" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    public HttpClient ClientFor(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<string> CreateItemAsync(string token, int price)
    {
        using var client = ClientFor(token);
        using var response = await client.PostAsJsonAsync("/api/items", new
        {
            contentId = "uffizi-botticelli-venere",
            targetAudience = "general",
            isPublic = true,
            price,
            descriptions = new[] { new { tone = "easy", text = "Integration test" } },
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("item").GetProperty("_id").GetString()!;
    }

    public async Task<int> BalanceAsync(string token)
    {
        using var client = ClientFor(token);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        return me.GetProperty("user").GetProperty("walletBalance").GetInt32();
    }

    /// <summary>Direct database access, for asserting what the API must never leave behind.</summary>
    public async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
