using System.Net;
using System.Net.Http.Json;
using Lynx.Api.Auth;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lynx.Api.Tests;

/// <summary>Boots the API against a throw-away SQLite file.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"lynx-test-{Guid.NewGuid():N}.db");

    public ApiFactory()
    {
        // Program reads the connection string while building, before ConfigureWebHost overrides apply.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", $"Data Source={_dbPath}");
    }

    public HttpClient CreateLynxClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(AuthSetup.CsrfHeader, "1");
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", null);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_dbPath) + "*"))
            try { File.Delete(f); } catch (IOException) { }
    }
}

public class AuthTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AuthTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Full_auth_flow()
    {
        var client = _factory.CreateLynxClient();

        var status = await client.GetFromJsonAsync<AuthStatus>("/api/auth/status");
        Assert.True(status!.SetupRequired);
        Assert.False(status.Authenticated);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ping")).StatusCode);

        var weak = await client.PostAsJsonAsync("/api/auth/setup", new Credentials("me@example.com", "short"));
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);

        var setup = await client.PostAsJsonAsync("/api/auth/setup", new Credentials("me@example.com", "correct-horse-battery"));
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ping")).StatusCode);

        var again = await client.PostAsJsonAsync("/api/auth/setup", new Credentials("other@example.com", "correct-horse-battery"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ping")).StatusCode);

        var bad = await client.PostAsJsonAsync("/api/auth/login", new Credentials("me@example.com", "wrong-password-1"));
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login", new Credentials("me@example.com", "correct-horse-battery"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ping")).StatusCode);

        status = await client.GetFromJsonAsync<AuthStatus>("/api/auth/status");
        Assert.True(status!.Authenticated);
        Assert.Equal("me@example.com", status.Email);
    }

    [Fact]
    public async Task State_changing_requests_without_csrf_header_are_rejected()
    {
        var client = _factory.CreateClient(); // no X-Lynx-Request header
        var response = await client.PostAsJsonAsync("/api/auth/login", new Credentials("a@b.c", "whatever-password"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
