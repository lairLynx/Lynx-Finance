using System.Threading.RateLimiting;
using Lynx.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace Lynx.Api.Auth;

public static class AuthSetup
{
    public const string LoginRateLimit = "login";
    public const string CsrfHeader = "X-Lynx-Request";

    /// <param name="secureCookies">Mark the login cookie Secure. Turn on whenever users reach the app over HTTPS (e.g. Tailscale Serve).</param>
    public static IServiceCollection AddLynxAuth(this IServiceCollection services, bool secureCookies = false)
    {
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.AddAuthorization();

        services.AddIdentityCore<IdentityUser>(options =>
            {
                options.Password.RequiredLength = 12; // length over composition rules: passphrases are fine
                options.Password.RequireDigit = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "lynx.auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;
            // The SPA handles navigation; the API only ever answers with status codes.
            options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LoginRateLimit, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
        });

        return services;
    }

    /// <summary>
    /// CSRF defence on top of SameSite=Strict: every state-changing /api request must carry a custom
    /// header, which a cross-site form or simple request cannot set.
    /// </summary>
    public static IApplicationBuilder UseCsrfHeaderCheck(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var unsafeMethod = !HttpMethods.IsGet(context.Request.Method)
                               && !HttpMethods.IsHead(context.Request.Method)
                               && !HttpMethods.IsOptions(context.Request.Method);
            if (unsafeMethod && context.Request.Path.StartsWithSegments("/api")
                             && !context.Request.Headers.ContainsKey(CsrfHeader))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            await next();
        });
}
