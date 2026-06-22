using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Pm.Api.Tests;

public class HealthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Health_returns_ok()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", body);
    }

    [Fact]
    public async Task HealthDb_returns_ok()
    {
        // WebApplicationFactory 啟動時會 Migrate 出本機 pm.sqlite,DB 可開
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health/db");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("\"db\":\"ok\"", await resp.Content.ReadAsStringAsync());
    }
}
