using Microsoft.AspNetCore.Identity;

namespace Lynx.Api.Auth;

public record Credentials(string Email, string Password);

public record AuthStatus(bool SetupRequired, bool Authenticated, string? Email);

public static class AuthEndpoints
{
    // Single-user app: serialise first-run setup so two requests can't both create an account.
    private static readonly SemaphoreSlim SetupLock = new(1, 1);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth");

        group.MapGet("/status", (UserManager<IdentityUser> users, HttpContext http) =>
        {
            var setupRequired = !users.Users.Any();
            var authenticated = http.User.Identity?.IsAuthenticated == true;
            return new AuthStatus(setupRequired, authenticated, authenticated ? http.User.Identity!.Name : null);
        });

        group.MapPost("/setup", async (Credentials body, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) =>
        {
            await SetupLock.WaitAsync();
            try
            {
                if (users.Users.Any())
                    return Results.Conflict(new { error = "Setup has already been completed." });

                var user = new IdentityUser { UserName = body.Email, Email = body.Email };
                var result = await users.CreateAsync(user, body.Password);
                if (!result.Succeeded)
                    return Results.BadRequest(new { errors = result.Errors.Select(e => e.Description) });

                await signIn.SignInAsync(user, isPersistent: true);
                return Results.Ok(new AuthStatus(false, true, user.UserName));
            }
            finally
            {
                SetupLock.Release();
            }
        });

        group.MapPost("/login", async (Credentials body, SignInManager<IdentityUser> signIn) =>
        {
            var result = await signIn.PasswordSignInAsync(body.Email, body.Password, isPersistent: true, lockoutOnFailure: true);
            if (result.IsLockedOut)
                return Results.Json(new { error = "Too many failed attempts. Try again in 15 minutes." }, statusCode: StatusCodes.Status423Locked);
            return result.Succeeded
                ? Results.Ok(new AuthStatus(false, true, body.Email))
                : Results.Json(new { error = "Invalid email or password." }, statusCode: StatusCodes.Status401Unauthorized);
        }).RequireRateLimiting(AuthSetup.LoginRateLimit);

        group.MapPost("/logout", async (SignInManager<IdentityUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        });

        return routes;
    }
}
