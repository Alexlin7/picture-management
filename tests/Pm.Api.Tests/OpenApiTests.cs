using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Pm.Api.Tests;

public class OpenApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OpenApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task OpenApi_document_is_served_and_lists_endpoints()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("\"openapi\"", body);     // 是 OpenAPI 文件
        Assert.Contains("/api/roots", body);      // 端點有被收進文件
        Assert.Contains("/api/tag/requeue", body);
    }

    [Fact]
    public async Task Scalar_ui_is_served()
    {
        // 注意:SPA fallback(MapFallbackToFile）會把無副檔名路徑接去 index.html 回 200,
        // 故不能只驗狀態碼;改驗回傳內容是 Scalar UI(index.html 不含 "scalar")。
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/scalar/v1");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("scalar", body, StringComparison.OrdinalIgnoreCase);
    }
}
