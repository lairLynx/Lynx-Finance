using System.Net;
using System.Net.Http.Json;

namespace Lynx.Api.Tests;

public class DeploymentTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public DeploymentTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Healthz_is_anonymous_and_reports_a_version()
    {
        var client = _factory.CreateClient(); // no login, no CSRF header: the deploy script and monitors call this plainly
        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("ok", body!["status"]);
        Assert.False(string.IsNullOrWhiteSpace(body["version"]));
    }

    [Fact]
    public async Task Responses_carry_basic_security_headers()
    {
        var response = await _factory.CreateClient().GetAsync("/healthz");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task Unknown_api_routes_are_not_swallowed_by_the_spa_fallback()
    {
        var response = await _factory.CreateClient().GetAsync("/api/does-not-exist");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
