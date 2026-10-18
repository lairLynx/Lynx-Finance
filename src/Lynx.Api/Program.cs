using System.Reflection;
using Lynx.Api.Auth;
using Lynx.Api.Finance;
using Lynx.Infrastructure;
using Lynx.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;

// As a Windows service the working directory is System32, so resolve wwwroot and config next to the executable.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null,
});
builder.Host.UseWindowsService(options => options.ServiceName = "LynxFinance");

// Lynx:DataDir (set by the installer) keeps the database and key ring outside the release folder, so deploys never touch them.
var dataDir = builder.Configuration["Lynx:DataDir"];

builder.Services.AddInfrastructure(ResolveConnectionString(builder, dataDir));
builder.Services.AddLynxAuth(secureCookies: builder.Configuration.GetValue<bool>("Lynx:SecureCookies"));
builder.Services.AddProblemDetails();
ConfigureDataProtection(builder.Services, dataDir);

// Behind Tailscale Serve (or any local reverse proxy) the real scheme and client address arrive in X-Forwarded-* headers.
// Only loopback proxies are trusted (the framework default), so a client cannot spoof them.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    await next();
});

// index.html must always be revalidated so a new deploy is picked up; hashed bundles are safe to cache by ETag.
var staticFiles = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name == "index.html") ctx.Context.Response.Headers.CacheControl = "no-cache";
    },
};
app.UseDefaultFiles();
app.UseStaticFiles(staticFiles);
app.UseRateLimiter();
app.UseCsrfHeaderCheck();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();

// Liveness + database check for the deploy script and monitoring; deliberately anonymous and free of personal data.
var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
app.MapGet("/healthz", async (AppDbContext db) =>
    await db.Database.CanConnectAsync()
        ? Results.Ok(new { status = "ok", version })
        : Results.Json(new { status = "database unavailable", version }, statusCode: StatusCodes.Status503ServiceUnavailable));

var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/ping", () => new { message = "pong" });
api.MapFinanceEndpoints();
api.MapOverviewEndpoints();
api.MapBudgetEndpoints();
api.MapGoalEndpoints();
api.MapRecurringEndpoints();

// In production the Angular build is served from wwwroot; unknown non-API routes fall back to the SPA.
app.MapFallbackToFile("{*path:regex(^(?!api/).*$)}", "index.html", staticFiles);

app.Run();

// Relative SQLite paths resolve against the content root, not whatever the working directory happens to be.
static string ResolveConnectionString(WebApplicationBuilder builder, string? dataDir)
{
    var cs = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(builder.Configuration.GetConnectionString("Default"));
    if (!string.IsNullOrWhiteSpace(dataDir))
        cs.DataSource = Path.Combine(dataDir, "lynx.db");
    else if (!Path.IsPathRooted(cs.DataSource))
        cs.DataSource = Path.Combine(builder.Environment.ContentRootPath, cs.DataSource);
    Directory.CreateDirectory(Path.GetDirectoryName(cs.DataSource)!);
    return cs.ToString();
}

// Without a persisted key ring, every restart or redeploy would invalidate the login cookie.
static void ConfigureDataProtection(IServiceCollection services, string? dataDir)
{
    if (string.IsNullOrWhiteSpace(dataDir)) return;

    var keys = Directory.CreateDirectory(Path.Combine(dataDir, "keys"));
    var protection = services.AddDataProtection().SetApplicationName("Lynx.Finance").PersistKeysToFileSystem(keys);
    if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi(protectToLocalMachine: true);
}

public partial class Program;
