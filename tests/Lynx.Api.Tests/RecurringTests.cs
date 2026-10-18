using System.Net;
using System.Net.Http.Json;
using Lynx.Api.Auth;
using Lynx.Api.Finance;
using Lynx.Application;
using Lynx.Domain.Entities;

namespace Lynx.Api.Tests;

public class RecurringDetectorTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    private static DetectorInput Tx(string date, long amount, string payee, int account = 1) => new(account, DateOnly.Parse(date), amount, payee);

    [Fact]
    public void Monthly_rent_is_detected_with_the_next_due_date()
    {
        var found = RecurringDetector.Detect(
            [Tx("2026-06-05", -115000, "Landlord"), Tx("2026-07-05", -115000, "Landlord"), Tx("2026-08-05", -115000, "Landlord"),
             Tx("2026-09-05", -115000, "Landlord"), Tx("2026-10-05", -115000, "Landlord")], Today);

        var rent = Assert.Single(found);
        Assert.Equal((RecurringFrequency.Monthly, -115000L, new DateOnly(2026, 11, 5)), (rent.Frequency, rent.AmountMinor, rent.NextDue));
        Assert.Equal(5, rent.Occurrences);
    }

    [Fact]
    public void Payee_names_with_terminal_ids_are_grouped_together()
    {
        var found = RecurringDetector.Detect(
            [Tx("2026-07-24", -1399, "NETFLIX.COM 12345 NL"), Tx("2026-08-24", -1399, "Netflix.com 99881 NL"), Tx("2026-09-24", -1399, "NETFLIX.COM 55 NL")], Today);

        var item = Assert.Single(found);
        Assert.Equal("-netflix com nl", item.PayeeKey);
        Assert.Equal(new DateOnly(2026, 10, 24), item.NextDue);
    }

    [Fact]
    public void Salary_with_small_variations_is_detected_as_income()
    {
        var found = RecurringDetector.Detect(
            [Tx("2026-08-06", 642700, "ACME SRL"), Tx("2026-09-04", 628400, "ACME SRL"), Tx("2026-10-06", 619500, "ACME SRL")], Today);

        var salary = Assert.Single(found);
        Assert.True(salary.AmountMinor > 0);
        Assert.Equal(RecurringFrequency.Monthly, salary.Frequency);
    }

    [Fact]
    public void Weekly_shopping_with_varying_amounts_is_not_recurring()
    {
        var found = RecurringDetector.Detect(
            [Tx("2026-09-12", -4000, "Lidl"), Tx("2026-09-19", -7000, "Lidl"), Tx("2026-09-26", -5200, "Lidl"), Tx("2026-10-03", -9100, "Lidl")], Today);
        Assert.Empty(found);
    }

    [Fact]
    public void A_cancelled_subscription_is_not_reported()
    {
        var found = RecurringDetector.Detect(
            [Tx("2026-01-10", -999, "Old Gym"), Tx("2026-02-10", -999, "Old Gym"), Tx("2026-03-10", -999, "Old Gym")], Today);
        Assert.Empty(found);
    }

    [Fact]
    public void Two_payments_a_year_apart_are_a_yearly_item()
    {
        // One payment is not a pattern.
        Assert.Empty(RecurringDetector.Detect([Tx("2025-11-12", -41200, "Car Insurance")], Today));

        var yearly = RecurringDetector.Detect([Tx("2024-11-12", -41200, "Car Insurance"), Tx("2025-11-12", -41200, "Car Insurance")], Today);
        var item = Assert.Single(yearly);
        Assert.Equal((RecurringFrequency.Yearly, new DateOnly(2026, 11, 12)), (item.Frequency, item.NextDue));
    }

    [Fact]
    public void Month_end_dates_do_not_drift()
    {
        var anchor = new DateOnly(2026, 1, 31);
        var dates = RecurringSchedule.Occurrences(anchor, RecurringFrequency.Monthly, new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 30)).ToList();
        Assert.Equal([new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 30)], dates);
    }
}

public class RecurringApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public RecurringApiTests(ApiFactory factory) => _factory = factory;

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

    [Fact]
    public async Task Detected_items_drive_the_forecast_only_once_confirmed()
    {
        var client = await SignedInClient();
        var account = (await (await client.PostAsJsonAsync("/api/accounts", new AccountInput("NOK main", null, null, "Current", "NOK", 1000m)))
            .Content.ReadFromJsonAsync<AccountDto>())!;

        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var monthsAgo in new[] { 4, 3, 2, 1 })
        {
            var r = await client.PostAsJsonAsync("/api/transactions",
                new TransactionInput(account.Id, today.AddMonths(-monthsAgo), "Gym Club", "", -50m, null));
            r.EnsureSuccessStatusCode();
        }

        var detect = (await (await client.PostAsync("/api/recurring/detect", null)).Content.ReadFromJsonAsync<DetectRecurringResult>())!;
        Assert.Equal(1, detect.Added);
        Assert.Equal(0, (await (await client.PostAsync("/api/recurring/detect", null)).Content.ReadFromJsonAsync<DetectRecurringResult>())!.Added); // idempotent

        var list = (await client.GetFromJsonAsync<RecurringList>("/api/recurring?currency=NOK"))!;
        var item = Assert.Single(list.Items);
        Assert.Equal(("Monthly", "Detected", -50m), (item.Frequency, item.Status, item.Amount));
        Assert.True(item.NextDue >= today);

        // Suggestions don't move the forecast.
        var before = (await client.GetFromJsonAsync<ForecastDto>("/api/forecast?currency=NOK&days=90&includeVariable=false"))!;
        Assert.Empty(before.Events);
        Assert.Equal(before.StartBalance, before.EndBalance);

        var confirm = await client.PutAsJsonAsync($"/api/recurring/{item.Id}", new RecurringInput(null, null, null, null, null, "Confirmed"));
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        var after = (await client.GetFromJsonAsync<ForecastDto>("/api/forecast?currency=NOK&days=90&includeVariable=false"))!;
        Assert.InRange(after.Events.Count, 2, 4);
        Assert.Equal(after.StartBalance + after.Events.Sum(e => e.Amount), after.EndBalance);
        Assert.Equal(91, after.Points.Count);
        Assert.True(after.Lowest <= after.StartBalance);

        // Everyday spending (not covered by a confirmed item) lowers the projection further.
        await client.PostAsJsonAsync("/api/transactions", new TransactionInput(account.Id, today.AddDays(-5), "Market", "", -90m, null));
        var withVariable = (await client.GetFromJsonAsync<ForecastDto>("/api/forecast?currency=NOK&days=90&includeVariable=true"))!;
        Assert.True(withVariable.DailyVariable < 0);

        // Dismissed items stay dismissed after a rescan.
        await client.PutAsJsonAsync($"/api/recurring/{item.Id}", new RecurringInput(null, null, null, null, null, "Dismissed"));
        await client.PostAsync("/api/recurring/detect", null);
        Assert.Equal("Dismissed", Assert.Single((await client.GetFromJsonAsync<RecurringList>("/api/recurring?currency=NOK"))!.Items).Status);
    }

    [Fact]
    public async Task Manual_items_are_validated_and_unique_per_account_and_payee()
    {
        var client = await SignedInClient();
        var account = (await (await client.PostAsJsonAsync("/api/accounts", new AccountInput("DKK main", null, null, "Current", "DKK", 0m)))
            .Content.ReadFromJsonAsync<AccountDto>())!;
        var due = DateOnly.FromDateTime(DateTime.Today).AddDays(10);

        var ok = await client.PostAsJsonAsync("/api/recurring", new RecurringInput(account.Id, "Rent", -800m, "Monthly", due, null));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal("Confirmed", (await ok.Content.ReadFromJsonAsync<RecurringDto>())!.Status);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/recurring", new RecurringInput(account.Id, "rent", -900m, "Monthly", due, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/recurring", new RecurringInput(account.Id, "Bad", -5m, "Daily", due, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/recurring", new RecurringInput(account.Id, "", -5m, "Monthly", due, null))).StatusCode);
    }
}
