using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;

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
    public async Task Static_files_are_served_and_only_extensionless_routes_fall_back_to_the_spa()
    {
        // Regression: a catch-all fallback once answered every request, including main-*.js, with index.html (blank page).
        var webRoot = Directory.CreateTempSubdirectory("lynx-wwwroot-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<html>spa-shell</html>");
            await File.WriteAllTextAsync(Path.Combine(webRoot, "main-ABC123.js"), "console.log('bundle');");
            var client = _factory.WithWebHostBuilder(b => b.UseWebRoot(webRoot)).CreateClient();

            var js = await client.GetAsync("/main-ABC123.js");
            Assert.Equal(HttpStatusCode.OK, js.StatusCode);
            Assert.Contains("javascript", js.Content.Headers.ContentType!.MediaType);
            Assert.Equal("console.log('bundle');", await js.Content.ReadAsStringAsync());

            Assert.Contains("spa-shell", await client.GetStringAsync("/"));
            Assert.Contains("spa-shell", await client.GetStringAsync("/dashboard"));           // client-side route
            Assert.Contains("spa-shell", await client.GetStringAsync("/transactions"));

            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/missing-file.js")).StatusCode); // not index.html
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/does-not-exist")).StatusCode);
        }
        finally { Directory.Delete(webRoot, recursive: true); }
    }

    [Fact]
    public async Task Unknown_api_routes_are_not_swallowed_by_the_spa_fallback()
    {
        var response = await _factory.CreateClient().GetAsync("/api/does-not-exist");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
