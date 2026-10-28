using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ArtAround.ContractTests;

public class HealthTests
{
    [Fact]
    public async Task GetHealth_ApiRunning_ReturnsOkStatus()
    {
        using var response = await Api.Client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }
}
