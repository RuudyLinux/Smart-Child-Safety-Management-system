using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;

namespace ChildGuard.Api.IntegrationTests;

public class HealthTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public HealthTests(ApiFactory api) => _api = api;

    [Fact]
    public async Task Live_AnswersWithoutSigningIn()
    {
        var response = await _api.CreateClient().GetAsync("/api/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", body.GetProperty("status").GetString());
        // Liveness says nothing about the database: it must not wake a sleeping one.
        Assert.False(body.TryGetProperty("database", out _));
    }

    [Fact]
    public async Task Health_StillReportsTheDatabase()
    {
        var body = await _api.CreateClient().GetFromJsonAsync<JsonElement>("/api/health");
        Assert.Equal("ok", body.GetProperty("database").GetString());
    }
}
