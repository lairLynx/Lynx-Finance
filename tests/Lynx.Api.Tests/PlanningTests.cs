using System.Net;
using System.Net.Http.Json;
using Lynx.Api.Auth;
using Lynx.Api.Finance;

namespace Lynx.Api.Tests;

public class PlanningTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public PlanningTests(ApiFactory factory) => _factory = factory;

    private async Task<HttpClient> SignedInClient()
    {
        var client = _factory.CreateLynxClient();
        var status = await client.GetFromJsonAsync<AuthStatus>("/api/auth/status");
        var creds = new Credentials("me@example.com", "correct-horse-battery");
        var response = status!.SetupRequired
            ? await client.PostAsJsonAsync("/api/auth/setup", creds)
            : await client.PostAsJsonAsync("/api/auth/login", creds);
        response.EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<AccountDto> NewAccount(HttpClient client, string name, string currency, decimal opening)
    {
        var r = await client.PostAsJsonAsync("/api/accounts", new AccountInput(name, null, null, "Current", currency, opening));
        return (await r.Content.ReadFromJsonAsync<AccountDto>())!;
    }

    [Fact]
    public async Task Budgets_track_real_spending_per_currency_and_can_be_copied()
    {
        var client = await SignedInClient();
        var chf = await NewAccount(client, "CHF main", "CHF", 0m);
        var eur = await NewAccount(client, "EUR other", "EUR", 0m);

        async Task Add(AccountDto account, string date, decimal amount, int? category, bool transfer = false)
        {
            var r = await client.PostAsJsonAsync("/api/transactions", new TransactionInput(account.Id, DateOnly.Parse(date), "Shop", "", amount, category, transfer));
            r.EnsureSuccessStatusCode();
        }

        await Add(chf, "2026-05-03", -40m, 1);
        await Add(chf, "2026-05-04", -10m, 1);
        await Add(chf, "2026-05-05", -25m, null);              // uncategorized
        await Add(chf, "2026-05-06", -100m, 1, transfer: true); // a transfer is not spending
        await Add(eur, "2026-05-07", -999m, 1);                 // other currency must not count

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/budgets", new BudgetInput("2026-05", 1, "CHF", 100m))).StatusCode);

        var may = (await client.GetFromJsonAsync<BudgetsDto>("/api/budgets?currency=CHF&month=2026-05"))!;
        var line = Assert.Single(may.Lines);
        Assert.Equal((100m, 50m), (line.Budget, line.Spent));
        Assert.Equal(75m, may.TotalSpent);
        Assert.Equal(25m, Assert.Single(may.Unbudgeted).Amount);
        Assert.Equal(100m, may.Budgeted);

        // Updating replaces the amount instead of adding a second row.
        await client.PutAsJsonAsync("/api/budgets", new BudgetInput("2026-05", 1, "CHF", 120m));
        Assert.Equal(120m, Assert.Single((await client.GetFromJsonAsync<BudgetsDto>("/api/budgets?currency=CHF&month=2026-05"))!.Lines).Budget);

        var june = (await client.GetFromJsonAsync<BudgetsDto>("/api/budgets?currency=CHF&month=2026-06"))!;
        Assert.Empty(june.Lines);
        Assert.True(june.CanCopy);

        var copied = await client.PostAsJsonAsync("/api/budgets/copy", new BudgetCopyInput("2026-05", "2026-06", "CHF"));
        Assert.Contains("\"copied\":1", await copied.Content.ReadAsStringAsync());
        var again = await client.PostAsJsonAsync("/api/budgets/copy", new BudgetCopyInput("2026-05", "2026-06", "CHF"));
        Assert.Contains("\"copied\":0", await again.Content.ReadAsStringAsync());

        // Amount 0 removes the budget; bad input is rejected.
        await client.PutAsJsonAsync("/api/budgets", new BudgetInput("2026-06", 1, "CHF", 0m));
        Assert.Empty((await client.GetFromJsonAsync<BudgetsDto>("/api/budgets?currency=CHF&month=2026-06"))!.Lines);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/budgets", new BudgetInput("June", 1, "CHF", 5m))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/budgets", new BudgetInput("2026-06", 999, "CHF", 5m))).StatusCode);
    }

    [Fact]
    public async Task Goals_follow_accounts_or_manual_contributions()
    {
        var client = await SignedInClient();
        var savings = await NewAccount(client, "Savings SEK", "SEK", 100m);
        await client.PostAsJsonAsync("/api/transactions", new TransactionInput(savings.Id, DateOnly.Parse("2026-01-02"), "Deposit", "", 50m, null));

        var due = DateOnly.FromDateTime(DateTime.Today).AddDays(365);

        // Manual goal: starts on track, then moves with contributions.
        var created = await client.PostAsJsonAsync("/api/goals", new GoalInput("Laptop", "sek", 1000m, due, null, 200m));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var manual = (await created.Content.ReadFromJsonAsync<GoalDto>())!;
        Assert.Equal(("SEK", 200m, 800m, "OnTrack"), (manual.Currency, manual.Saved, manual.Remaining, manual.Status));
        Assert.NotNull(manual.MonthlyNeeded);
        Assert.InRange(manual.MonthlyNeeded!.Value, 60m, 70m); // 800 over ~12 months

        var after = (await (await client.PostAsJsonAsync($"/api/goals/{manual.Id}/contribute", new ContributionInput(300m))).Content.ReadFromJsonAsync<GoalDto>())!;
        Assert.Equal(500m, after.Saved);
        var withdrawn = (await (await client.PostAsJsonAsync($"/api/goals/{manual.Id}/contribute", new ContributionInput(-9999m))).Content.ReadFromJsonAsync<GoalDto>())!;
        Assert.Equal(0m, withdrawn.Saved); // never below zero

        // Linked goal: saved is the live account balance and the currency comes from the account.
        var linked = (await (await client.PostAsJsonAsync("/api/goals", new GoalInput("Rainy day", "USD", 400m, null, savings.Id, null))).Content.ReadFromJsonAsync<GoalDto>())!;
        Assert.Equal(("SEK", 150m, "NoDate"), (linked.Currency, linked.Saved, linked.Status));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/goals/{linked.Id}/contribute", new ContributionInput(10m))).StatusCode);

        await client.PostAsJsonAsync("/api/transactions", new TransactionInput(savings.Id, DateOnly.Parse("2026-02-02"), "Deposit", "", 300m, null));
        var goals = (await client.GetFromJsonAsync<List<GoalDto>>("/api/goals"))!;
        Assert.Equal("Completed", goals.Single(g => g.Id == linked.Id).Status); // 450 >= 400

        // A deadline in the past with money still missing is behind.
        var overdue = (await (await client.PostAsJsonAsync("/api/goals", new GoalInput("Old", "SEK", 500m, DateOnly.FromDateTime(DateTime.Today).AddDays(-1), null, 10m))).Content.ReadFromJsonAsync<GoalDto>())!;
        Assert.Equal("Behind", overdue.Status);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/goals", new GoalInput("", "SEK", 10m, null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/goals", new GoalInput("Zero", "SEK", 0m, null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/goals/{overdue.Id}")).StatusCode);
    }
}
