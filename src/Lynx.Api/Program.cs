using Lynx.Api.Auth;
using Lynx.Infrastructure;
using Lynx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(ResolveConnectionString(builder));
builder.Services.AddLynxAuth();
builder.Services.AddProblemDetails();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseCsrfHeaderCheck();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();

var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/ping", () => new { message = "pong" });

// In production the Angular build is served from wwwroot; unknown non-API routes fall back to the SPA.
app.MapFallbackToFile("{*path:regex(^(?!api/).*$)}", "index.html");

app.Run();

// Relative SQLite paths resolve against the content root, not whatever the working directory happens to be.
static string ResolveConnectionString(WebApplicationBuilder builder)
{
    var cs = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(builder.Configuration.GetConnectionString("Default"));
    if (!Path.IsPathRooted(cs.DataSource))
        cs.DataSource = Path.Combine(builder.Environment.ContentRootPath, cs.DataSource);
    Directory.CreateDirectory(Path.GetDirectoryName(cs.DataSource)!);
    return cs.ToString();
}

public partial class Program;
