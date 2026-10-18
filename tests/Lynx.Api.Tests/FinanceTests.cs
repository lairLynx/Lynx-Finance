using System.Net;
using System.Net.Http.Json;
using System.Text;
using Lynx.Api.Auth;
using Lynx.Api.Finance;
using Lynx.Application.Import;

// ApiFactory passes its temp DB path through an environment variable, so fixtures must not overlap.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Lynx.Api.Tests;

public class ParserTests
{
    [Theory]
    [InlineData("1.234,56", 123456)]
    [InlineData("1,234.56", 123456)]
    [InlineData("-12,50", -1250)]
    [InlineData("(12.50)", -1250)]
    [InlineData("€ 3 650,00", 365000)]
    [InlineData("1,234", 123400)]
    [InlineData("42", 4200)]
    public void Amounts_parse_across_locales(string raw, long expected) =>
        Assert.Equal(expected, AmountParser.ParseMinor(raw));

    [Fact]
    public void Revolut_csv_is_parsed_and_pending_rows_skipped()
    {
        const string csv =
            "Type,Product,Started Date,Completed Date,Description,Amount,Fee,Currency,State,Balance\n" +
            "CARD_PAYMENT,Current,2026-10-01 10:00:00,2026-10-02 09:00:00,Lidl,-12.40,0.00,EUR,COMPLETED,100.00\n" +
            "TOPUP,Current,2026-10-03 10:00:00,2026-10-03 10:00:01,Top-up,200.00,0.00,EUR,COMPLETED,300.00\n" +
            "CARD_PAYMENT,Current,2026-10-04 10:00:00,,Cafe,-3.00,0.00,EUR,PENDING,297.00\n";
        var rows = CsvStatementParser.Parse(csv);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new DateOnly(2026, 10, 2), rows[0].Date);
        Assert.Equal(-1240, rows[0].AmountMinor);
        Assert.Equal("Lidl", rows[0].Payee);
    }

    [Fact]
    public void Semicolon_csv_with_debit_credit_and_romanian_headers()
    {
        const string csv =
            "Cont;12345\n" +
            "Data tranzacției;Descriere;Debit;Credit\n" +
            "05.10.2026;\"Plată, Kaufland\";123,45;\n" +
            "06.10.2026;Salariu;;3.650,00\n";
        var rows = CsvStatementParser.Parse(csv);
        Assert.Equal(2, rows.Count);
        Assert.Equal(-12345, rows[0].AmountMinor);
        Assert.Equal("Plată, Kaufland", rows[0].Payee);
        Assert.Equal(365000, rows[1].AmountMinor);
    }

    [Fact]
    public void Ing_romania_multirow_statement_is_parsed()
    {
        // Synthetic data in the layout of an ING Romania "Extras de cont" export; the third block has shifted columns.
        const string csv =
            "Titular cont: DL Test Person,,,,,,,,,\n" +
            "\"Str. -, Nr. 1\n\n123456, Oras, RO\",,,,,,,,,\n" +
            ",Data,,,Detalii tranzactie,,,Debit,,Credit\n" +
            "04 octombrie 2026,,,Incasare,,,,,\"150,00\",\n" +
            ",,,Data: 04-10-2026,,,,,,\n" +
            ",,,Ordonator:MARIA POPESCU,,,,,,\n" +
            ",,,Detalii:C,,,,,,\n" +
            "06 octombrie 2026,,,Cumparare POS,,,\"39,58\",,,\n" +
            ",,,Numar card:**** 1111,,,,,,\n" +
            ",,,Tranzactie la:Froo  RO  Municipiul Br,,,,,,\n" +
            "06 octombrie 2026,,,Incasare,,,,,\"6.195,00\",\n" +
            ",,,Ordonator:ACME SRL,,,,,,\n" +
            ",,,Detalii:Salariu septembrie,,,,,,\n" +
            ",Sef Serviciu,,,,Alta Persoana,,,,\n" +
            "Titular cont: DL Test Person,,,,,,,,,,\n" +
            ",Data,,,,Detalii tranzactie,,,Debit,,Credit\n" +
            "15 octombrie 2026,,,,Transfer Home'Bank,,,\"1.397,29\",,,\n" +
            ",,,,Beneficiar:Test Person,,,,,,\n" +
            "18 octombrie 2026,,,,Incasare,,,,,\"914,05\",\n" +
            ",,,,Ordonator:TEST PERSON,,,,,,\n" +
            "Sold initial:,,,\"0,00\",,,,,,,\n";

        var rows = StatementParser.Parse("extras.csv", Encoding.UTF8.GetBytes(csv));

        Assert.Equal(5, rows.Count);
        Assert.Equal((new DateOnly(2026, 10, 4), 15000L, "MARIA POPESCU"), (rows[0].Date, rows[0].AmountMinor, rows[0].Payee));
        Assert.Equal((-3958L, "Froo RO Municipiul Br"), (rows[1].AmountMinor, rows[1].Payee));
        Assert.Null(rows[1].CounterpartyIban);
        Assert.Equal((619500L, "ACME SRL"), (rows[2].AmountMinor, rows[2].Payee));
        Assert.Contains("Salariu septembrie", rows[2].Description);
        Assert.Equal((-139729L, "Test Person"), (rows[3].AmountMinor, rows[3].Payee));
        Assert.Equal(91405L, rows[4].AmountMinor);
    }

    [Fact]
    public void Ing_romania_ignores_a_running_balance_column()
    {
        const string csv =
            "Titular cont: DL Test Person,,,,,,,,,,\n" +
            ",Data,,,Detalii tranzactie,,,Debit,,Credit,Sold\n" +
            "04 octombrie 2026,,,Incasare,,,,,\"150,00\",,\"150,00\"\n" +
            ",,,Ordonator:MARIA POPESCU,,,,,,,\n" +
            "05 octombrie 2026,,,Transfer Home'Bank,,,\"99,09\",,,,\"50,91\"\n" +
            ",,,Beneficiar:Test Person,,,,,,,\n";

        var rows = StatementParser.Parse("extras.csv", Encoding.UTF8.GetBytes(csv));

        Assert.Equal([15000L, -9909L], rows.Select(r => r.AmountMinor).ToArray());
    }

    private static byte[] FakePdf(params (double X, double Y, string Text)[][] pages)
    {
        // Uncompressed content streams in the same operator style as BT's generator.
        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        var id = 1;
        foreach (var page in pages)
        {
            sb.Append($"{id++} 0 obj\n<< /Length 0 >>\nstream\n");
            foreach (var (x, y, text) in page)
            {
                var escaped = text.Replace("(", "\\(").Replace(")", "\\)");
                sb.Append(System.FormattableString.Invariant($"BT 8 0 0 8 {x} {y} Tm /TT1 1 Tf ({escaped}) Tj ET\n"));
            }
            sb.Append("endstream\nendobj\n");
        }
        sb.Append("%%EOF");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    [Fact]
    public void Bt_pdf_statement_is_parsed_across_pages()
    {
        var page1 = new (double, double, string)[]
        {
            (483, 828, "BANCA TRANSILVANIA"), (22, 639, "EXTRAS CONT"),
            (22, 597, "Data"), (89, 597, "Descriere "), (448, 597, " Debit"), (545, 597, "Credit"),
            (89, 583, "SOLD ANTERIOR"), (554, 583, "0.00"),
            (90, 568, "Incasare Instant"), (523, 568, "      6,284.00"), (28, 567, "04/09/2026"),
            (90, 558, "Test Sender;RO30INGB0000999919070280;INGBROBU;"), (90, 547, "REF: 000ZEXA2624761C5 "),
            (90, 537, "Comision Plata Instant"), (457, 537, "2.50"), (90, 526, "REF: 208ZEXA2624701KP "),
            (90, 516, "Plata Instant"), (447, 516, "222.00"),
            (90, 505, "Rafa;Vp4b;Test Shop;RO24REVO0000141508674070;REVOROBB;"), (90, 495, "REF: 208ZEXA2624701KP "),
            (28, 273, "04/09/2026"), (89, 273, "RULAJ ZI"), (437, 273, "1,360.81"), (535, 273, "6,284.00"),
            (89, 260, "SOLD FINAL ZI"), (535, 261, "4,923.19"),
            (22, 31, "BANCA TRANSILVANIA S.A. "), (558, 10, "1"),
        };
        var page2 = new (double, double, string)[]
        {
            (483, 828, "BANCA TRANSILVANIA"), (22, 753, "TEST PERSON"),
            (90, 700, "Cumparare POS"), (452, 700, "1,166.76"), (28, 699, "05/09/2026"),
            (90, 690, "TID:12345678 SOME SHOP   CLUJ NAPOCA RO 46091995"),
            (89, 650, "SOLD FINAL CONT"), (535, 650, "3,694.93"),
            (258, 600, "SUME BLOCATE"), (60, 580, " 65.00 RON aferenta tranzactiei POS"), (535, 560, "99.00"),
        };

        var rows = StatementParser.Parse("statement.pdf", FakePdf(page1, page2));

        Assert.Equal(4, rows.Count);
        Assert.Equal((new DateOnly(2026, 9, 4), 628400L, "Test Sender"), (rows[0].Date, rows[0].AmountMinor, rows[0].Payee));
        Assert.Equal("RO30INGB0000999919070280", rows[0].CounterpartyIban);
        Assert.Equal("RO24REVO0000141508674070", rows[2].CounterpartyIban);
        Assert.Equal((-250L, "Banca Transilvania"), (rows[1].AmountMinor, rows[1].Payee));
        Assert.Equal((-22200L, "Test Shop", "Plata Instant — Rafa"), (rows[2].AmountMinor, rows[2].Payee, rows[2].Description));
        Assert.Equal((new DateOnly(2026, 9, 5), -116676L, "SOME SHOP CLUJ NAPOCA RO"), (rows[3].Date, rows[3].AmountMinor, rows[3].Payee));
    }

    [Fact]
    public void Unknown_pdf_is_rejected_with_a_clear_message()
    {
        var pdf = FakePdf([(22, 600, "Some other bank statement")]);
        var ex = Assert.Throws<ImportFormatException>(() => StatementParser.Parse("x.pdf", pdf));
        Assert.Contains("Banca Transilvania", ex.Message);
    }

    [Fact]
    public void Ofx_is_parsed()
    {
        const string ofx = "OFXHEADER:100\n<OFX><BANKTRANLIST><STMTTRN><TRNTYPE>DEBIT<DTPOSTED>20261005120000<TRNAMT>-64.20<FITID>A1<NAME>Albert Heijn<MEMO>Groceries</STMTTRN></BANKTRANLIST></OFX>";
        var rows = StatementParser.Parse("x.ofx", Encoding.UTF8.GetBytes(ofx));
        var row = Assert.Single(rows);
        Assert.Equal(-6420, row.AmountMinor);
        Assert.Equal("A1", row.ExternalId);
        Assert.Equal("Albert Heijn", row.Payee);
    }

    [Fact]
    public void Camt_is_parsed_and_rejects_dtd()
    {
        const string camt = """
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02"><BkToCstmrStmt><Stmt>
              <Ntry><Amt Ccy="EUR">78.50</Amt><CdtDbtInd>DBIT</CdtDbtInd><BookgDt><Dt>2026-10-07</Dt></BookgDt>
                <NtryDtls><TxDtls><RltdPties><Cdtr><Nm>Restaurant De Kas</Nm></Cdtr></RltdPties><RmtInf><Ustrd>Dinner</Ustrd></RmtInf></TxDtls></NtryDtls></Ntry>
            </Stmt></BkToCstmrStmt></Document>
            """;
        var row = Assert.Single(StatementParser.Parse("s.xml", Encoding.UTF8.GetBytes(camt)));
        Assert.Equal(-7850, row.AmountMinor);
        Assert.Equal("Restaurant De Kas", row.Payee);

        const string evil = "<!DOCTYPE d [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><Document><Ntry/></Document>";
        Assert.Throws<ImportFormatException>(() => CamtParser.Parse(evil));
    }
}

public class FinanceTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public FinanceTests(ApiFactory factory) => _factory = factory;

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
    public async Task Accounts_transactions_and_import_flow()
    {
        var client = await SignedInClient();

        var categories = await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories");
        Assert.Contains(categories!, c => c.Name == "Groceries");

        var created = await client.PostAsJsonAsync("/api/accounts", new AccountInput("Main", "ING", "NL00 TEST 0000 0000 00", "Current", "EUR", 1000m));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var account = (await created.Content.ReadFromJsonAsync<AccountDto>())!;
        Assert.Equal(1000m, account.Balance);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/accounts", new AccountInput("", null, null, "Current", "EUR", 0))).StatusCode);

        const string csv = "Date,Description,Amount\n2026-10-01,Coffee,-3.50\n2026-10-01,Coffee,-3.50\n2026-10-02,Salary,2000.00\n";
        async Task<ImportResult> Import()
        {
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "statement.csv");
            var response = await client.PostAsync($"/api/accounts/{account.Id}/import", form);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ImportResult>())!;
        }

        var first = await Import();
        Assert.Equal(3, first.Imported); // two identical coffees on the same day are both kept
        var second = await Import();
        Assert.Equal(0, second.Imported);
        Assert.Equal(3, second.Duplicates);

        var accounts = await client.GetFromJsonAsync<List<AccountDto>>("/api/accounts");
        Assert.Equal(1000m - 7m + 2000m, accounts!.Single(a => a.Id == account.Id).Balance);

        var page = await client.GetFromJsonAsync<TransactionPage>($"/api/transactions?accountId={account.Id}&q=coffee");
        Assert.Equal(2, page!.Total);

        var tx = page.Items[0];
        var groceries = categories!.Single(c => c.Name == "Groceries");
        var patched = await client.PatchAsJsonAsync($"/api/transactions/{tx.Id}/category", new FinanceEndpoints.CategoryAssignment(groceries.Id));
        Assert.Equal(groceries.Id, (await patched.Content.ReadFromJsonAsync<TransactionDto>())!.CategoryId);

        var uncategorized = await client.GetFromJsonAsync<TransactionPage>($"/api/transactions?accountId={account.Id}&uncategorized=true");
        Assert.Equal(2, uncategorized!.Total);

        var bad = await client.PostAsJsonAsync($"/api/accounts/{account.Id}/import", new { });
        Assert.False(bad.IsSuccessStatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/accounts/{account.Id}")).StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<TransactionPage>($"/api/transactions?accountId={account.Id}"))!.Total);
    }

    [Fact]
    public async Task Overview_keeps_currencies_separate()
    {
        var client = await SignedInClient();
        async Task<AccountDto> NewAccount(string name, string currency, decimal opening)
        {
            var r = await client.PostAsJsonAsync("/api/accounts", new AccountInput(name, null, null, "Current", currency, opening));
            return (await r.Content.ReadFromJsonAsync<AccountDto>())!;
        }
        async Task Add(AccountDto account, string date, string payee, decimal amount, int? category = null)
        {
            var r = await client.PostAsJsonAsync("/api/transactions", new TransactionInput(account.Id, DateOnly.Parse(date), payee, "", amount, category));
            r.EnsureSuccessStatusCode();
        }

        var ron = await NewAccount("RON main", "lei", 100m); // "lei" is normalised to RON
        var eur = await NewAccount("EUR main", "EUR", 1000m);
        Assert.Equal("RON", ron.Currency);

        await Add(ron, "2026-08-10", "Salary", 5000m);
        await Add(ron, "2026-09-05", "Shop", -200m, 1);
        await Add(ron, "2026-09-06", "Shop", -50m);
        await Add(eur, "2026-09-07", "Cafe", -12m);

        var o = (await client.GetFromJsonAsync<OverviewDto>("/api/overview?currency=RON&month=2026-09&months=3"))!;
        Assert.Equal(["EUR", "RON"], o.Currencies);
        Assert.Equal("RON", o.Currency);
        Assert.Equal(250m, o.Expense);
        Assert.Equal(0m, o.Income);
        Assert.Equal(4850m, o.NetWorth);          // 100 + 5000 - 250, EUR account excluded
        Assert.Equal(5100m, o.PreviousNetWorth);  // end of August
        Assert.Equal(["2026-07", "2026-08", "2026-09"], o.Months.Select(m => m.Month));
        Assert.Equal([100m, 5100m, 4850m], o.Balances.Select(b => b.Balance));
        Assert.Equal(1, o.UncategorizedCount);
        Assert.Equal(2, o.Categories.Count);
        Assert.Equal(200m, o.Categories[0].Amount);

        var e = (await client.GetFromJsonAsync<OverviewDto>("/api/overview?currency=EUR&month=2026-09"))!;
        Assert.Equal(988m, e.NetWorth);
        Assert.Equal(12m, e.Expense);
        Assert.DoesNotContain(e.Accounts, a => a.Currency == "RON");

        var fallback = (await client.GetFromJsonAsync<OverviewDto>("/api/overview?currency=USD"))!;
        Assert.Equal("RON", fallback.Currency); // unknown currency falls back to the one with most activity
        Assert.Equal("2026-09", fallback.Month); // defaults to the latest month with data
    }

    [Fact]
    public async Task Transfers_are_paired_marked_and_excluded_from_cash_flow()
    {
        var client = await SignedInClient();
        async Task<AccountDto> NewAccount(string name, string? iban)
        {
            var r = await client.PostAsJsonAsync("/api/accounts", new AccountInput(name, null, iban, "Current", "GBP", 0m));
            return (await r.Content.ReadFromJsonAsync<AccountDto>())!;
        }
        async Task<TransactionDto> Add(AccountDto account, string date, string payee, decimal amount)
        {
            var r = await client.PostAsJsonAsync("/api/transactions", new TransactionInput(account.Id, DateOnly.Parse(date), payee, "", amount, null));
            return (await r.Content.ReadFromJsonAsync<TransactionDto>())!;
        }

        var a = await NewAccount("GBP A", "GB29 NWBK 6016 1331 9268 19");
        var b = await NewAccount("GBP B", null);
        await Add(a, "2026-03-01", "Pay", 1000m);
        var sent = await Add(a, "2026-03-10", "Move to B", -300m);
        var received = await Add(b, "2026-03-12", "From A", 300m);   // two days later, other account
        var coffee = await Add(a, "2026-03-11", "Latte Bar", -300m);      // same amount, same account: must not pair with itself

        var detected = (await (await client.PostAsync("/api/transfers/detect", null)).Content.ReadFromJsonAsync<DetectResult>())!;
        Assert.Equal(2, detected.Marked);

        var page = (await client.GetFromJsonAsync<TransactionPage>($"/api/transactions?accountId={a.Id}&transfer=true"))!;
        Assert.Equal([sent.Id], page.Items.Select(t => t.Id));
        Assert.DoesNotContain(page.Items, t => t.Id == coffee.Id);

        var o = (await client.GetFromJsonAsync<OverviewDto>("/api/overview?currency=GBP&month=2026-03"))!;
        Assert.Equal(1000m, o.Income);     // the 300 received on B is not income
        Assert.Equal(300m, o.Expense);     // only the cafe; the 300 sent to B is not spending
        Assert.Equal(300m, o.Transfers);
        Assert.Equal(700m, o.NetWorth);    // balances still include everything: +1000 -300 +300 -300

        // The user's decision sticks: unmark, detect again, stays unmarked.
        var unmark = await client.PatchAsJsonAsync($"/api/transactions/{received.Id}/transfer", new TransferChange(false, false));
        Assert.Equal(1, (await unmark.Content.ReadFromJsonAsync<TransferResult>())!.Updated);
        await client.PostAsync("/api/transfers/detect", null);
        var again = (await client.GetFromJsonAsync<TransactionPage>($"/api/transactions?accountId={b.Id}&transfer=true"))!;
        Assert.Empty(again.Items);

        // Mark every row from one payee at once.
        var bulk = await client.PatchAsJsonAsync($"/api/transactions/{coffee.Id}/transfer", new TransferChange(true, true));
        Assert.Equal(1, (await bulk.Content.ReadFromJsonAsync<TransferResult>())!.Updated);
    }

    [Fact]
    public async Task Import_marks_transfers_to_own_iban()
    {
        var client = await SignedInClient();
        var own = await (await client.PostAsJsonAsync("/api/accounts", new AccountInput("Own BT", null, "RO09BTRLRONCRT0DG0000001", "Current", "RON", 0m))).Content.ReadFromJsonAsync<AccountDto>();
        var ing = await (await client.PostAsJsonAsync("/api/accounts", new AccountInput("Own ING", null, "RO30INGB0000999900000002", "Current", "RON", 0m))).Content.ReadFromJsonAsync<AccountDto>();

        const string csv = """
            Titular cont: DL Test,,,,,,,,,
            ,Data,,,Detalii tranzactie,,,Debit,,Credit
            05 octombrie 2026,,,Transfer Home'Bank,,,"99,09",,,
            ,,,Beneficiar:Test Person,,,,,,
            ,,,In contul:RO09BTRLRONCRT0DG0000001,,,,,,
            06 octombrie 2026,,,Transfer Home'Bank,,,"20,00",,,
            ,,,Beneficiar:Someone Else,,,,,,
            ,,,In contul:RO99XXXX0000000000000009,,,,,,
            """;
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "ing.csv");
        var response = await client.PostAsync($"/api/accounts/{ing!.Id}/import", form);
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<ImportResult>())!;

        Assert.Equal(2, result.Imported);
        Assert.Equal(1, result.TransfersMarked); // only the one going to an IBAN we own
        var marked = (await client.GetFromJsonAsync<TransactionPage>($"/api/transactions?accountId={ing.Id}&transfer=true"))!;
        Assert.Equal(-99.09m, Assert.Single(marked.Items).Amount);
        Assert.NotNull(own);
    }

    [Fact]
    public async Task Category_names_are_unique()
    {
        var client = await SignedInClient();
        var dupe = await client.PostAsJsonAsync("/api/categories", new CategoryInput("Groceries", "#112233", false));
        Assert.Equal(HttpStatusCode.Conflict, dupe.StatusCode);
        var badColor = await client.PostAsJsonAsync("/api/categories", new CategoryInput("Pets", "red", false));
        Assert.Equal(HttpStatusCode.BadRequest, badColor.StatusCode);
    }
}
