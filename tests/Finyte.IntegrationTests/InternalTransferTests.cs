using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Finyte.Core.Accounts;
using Finyte.Core.Analytics;
using Finyte.Core.Billing;
using Finyte.Core.Transfers;
using Finyte.Data;
using Finyte.Data.Analytics;
using Finyte.Data.Transfers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class InternalTransferTests
{
    [Theory]
    [InlineData("Transfer to xx6486 CommBank app", true)]
    [InlineData("Transfer to 12346486", true)]
    [InlineData("NetBank transfer 062-000 12346486 rent", true)]
    [InlineData("Transfer to ****6486", true)]
    [InlineData("Transfer to 12346486 6486", true)]
    [InlineData("Woolworths Card xx6486 Value Date: 01/09/2026", false)]
    [InlineData("WOOLWORTHS METRO 6486", false)]
    [InlineData("BPAY 6486 invoice", false)]
    [InlineData("Transfer to xx9999", false)]
    [InlineData("Transfer to savings", false)]
    [InlineData("Transfer to xx0449", false)]
    public void DetectorReadsTheCounterpartyAccountFromTheDescription(string description, bool expected)
    {
        var tenantId = Guid.NewGuid();
        var everyday = new Account { TenantId = tenantId, Name = "Everyday", AccountNumber = "12340449" };
        var savings = new Account { TenantId = tenantId, Name = "Savings", AccountNumber = "12346486" };
        var transaction = Transaction(tenantId, everyday.Id, -100, 1, description);

        var counterparty = InternalTransferDetector.Detect(transaction, [everyday, savings]);

        Assert.Equal(expected ? savings.Id : null, counterparty);
    }

    [Theory]
    [InlineData("Transfer To J Citizen CommBank App mortgage", true)]
    [InlineData("TRANSFER TO HOME LOAN OFFSET", true)]
    [InlineData("Transfer to mortgages r us", false)]
    [InlineData("Home loans expo ticket", false)]
    public void DetectorMatchesUserTransferNicknamesAsWholePhrases(string description, bool expected)
    {
        var tenantId = Guid.NewGuid();
        var everyday = new Account { TenantId = tenantId, Name = "Everyday", AccountNumber = "12340449" };
        var mortgage = new Account { TenantId = tenantId, Name = "Mortgage", TransferNicknames = ["mortgage", " Home  loan "] };
        var transaction = Transaction(tenantId, everyday.Id, -100, 1, description);

        Assert.Equal(expected ? mortgage.Id : null, InternalTransferDetector.Detect(transaction, [everyday, mortgage]));
    }

    [Theory]
    [InlineData("Transfer To J Citizen CommBank App mortgage", "Transfer To J Citizen CommBank App mortgage")]
    [InlineData("Fast Transfer 12/09/2026 To: Home Loan Ref 998877", "Fast Transfer To Home Loan Ref")]
    [InlineData("Transfer to xx6486", null)]
    [InlineData("Osko", null)]
    [InlineData("", null)]
    public void RulePhraseDropsNumbersAndNeedsSomethingToMatchOn(string description, string? expected)
    {
        Assert.Equal(expected, InternalTransferDetector.RulePhrase(description));
    }

    [Fact]
    public void MergeRuleShrinksToTheSharedPrefixOfTwoExamples()
    {
        var first = InternalTransferDetector.MergeRule([], "Fast Transfer From DELAN DASANAYAKE MUDI food")!;
        Assert.Equal(["Fast Transfer From DELAN DASANAYAKE MUDI food"], first);
        Assert.Null(InternalTransferDetector.MergeRule(first, "Fast Transfer From DELAN DASANAYAKE MUDI food"));
        var second = InternalTransferDetector.MergeRule(first, "Fast Transfer From DELAN DASANAYAKE MUDI rent")!;
        Assert.Equal(["Fast Transfer From DELAN DASANAYAKE MUDI"], second);
        Assert.Null(InternalTransferDetector.MergeRule(second, "Fast Transfer From DELAN DASANAYAKE MUDI groceries"));
        var unrelated = InternalTransferDetector.MergeRule(second, "Fast Transfer From SOMEONE ELSE")!;
        Assert.Equal(["Fast Transfer From DELAN DASANAYAKE MUDI", "Fast Transfer From SOMEONE ELSE"], unrelated);
    }

    [Fact]
    public void DetectorRefusesAmbiguousAndForeignMatches()
    {
        var tenantId = Guid.NewGuid();
        var everyday = new Account { TenantId = tenantId, Name = "Everyday", AccountNumber = "1111" };
        var savings = new Account { TenantId = tenantId, Name = "Savings", AccountNumber = "12346486" };
        var offset = new Account { TenantId = tenantId, Name = "Offset", AccountNumber = "99996486" };
        var foreign = new Account { TenantId = Guid.NewGuid(), Name = "Someone else", AccountNumber = "55556486" };
        var transaction = Transaction(tenantId, everyday.Id, -100, 1, "Transfer to xx6486");

        Assert.Null(InternalTransferDetector.Detect(transaction, [everyday, savings, offset]));
        Assert.Null(InternalTransferDetector.Detect(transaction, [everyday, foreign]));
        Assert.Equal(savings.Id, InternalTransferDetector.Detect(transaction, [everyday, savings, foreign]));
    }

    [Fact]
    public void ApplyLeavesManualDecisionsAlone()
    {
        var tenantId = Guid.NewGuid();
        var everyday = new Account { TenantId = tenantId, Name = "Everyday", AccountNumber = "12340449" };
        var savings = new Account { TenantId = tenantId, Name = "Savings", AccountNumber = "12346486" };
        var excluded = Transaction(tenantId, everyday.Id, -100, 1, "Transfer to xx6486");
        excluded.InternalTransferSource = InternalTransferDetector.Excluded;
        var manual = Transaction(tenantId, savings.Id, -100, 1, "Unlabelled");
        manual.InternalTransferAccountId = everyday.Id;
        manual.InternalTransferSource = InternalTransferDetector.Manual;
        var detected = Transaction(tenantId, everyday.Id, -100, 1, "Transfer to xx6486");

        Assert.False(InternalTransferDetector.Apply(excluded, [everyday, savings]));
        Assert.Null(excluded.InternalTransferAccountId);
        Assert.False(InternalTransferDetector.Apply(manual, [everyday, savings]));
        Assert.Equal(everyday.Id, manual.InternalTransferAccountId);
        Assert.True(InternalTransferDetector.Apply(detected, [everyday, savings]));
        Assert.Equal(savings.Id, detected.InternalTransferAccountId);
        Assert.Equal(InternalTransferDetector.Detected, detected.InternalTransferSource);
        Assert.False(InternalTransferDetector.Apply(detected, [everyday, savings]));
    }

    [Fact]
    public async Task DetectedTransfersAreExcludedFromTotalsOnEachLegIndependently()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var before = await CashFlow(client);
        Assert.Equal(2500, before.DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
        Assert.Equal(0, before.DailyCashFlow.Sum(x => x.IncomeMinorUnits));
        var raw = await client.GetFromJsonAsync<CashFlowRangeResponse>("/api/cash-flow?from=2026-08-01&to=2026-09-30&includeInternalTransfers=true");
        Assert.Equal(12500, raw!.DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
        Assert.Equal(10000, raw.DailyCashFlow.Sum(x => x.IncomeMinorUnits));
        var transfers = await GetReview(client);
        Assert.Equal(2, transfers.TotalCount);
        Assert.All(transfers.Items, x => Assert.Equal("detected", x.Source));
        Assert.Contains(transfers.Items, x => x.Id == seed.DebitId && x.CounterpartyAccountId == seed.CreditAccountId);
        Assert.Contains(transfers.Items, x => x.Id == seed.CreditId && x.CounterpartyAccountId == seed.DebitAccountId);
        var page = await client.GetFromJsonAsync<JsonElement>("/api/transactions?internalTransfers=only");
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        Assert.Equal("Savings", page.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == seed.DebitId).GetProperty("internalTransferAccountName").GetString());
    }

    [Fact]
    public async Task ManualDecisionsChangeTotalsInvalidateProjectionsAndSurviveReclassification()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        async Task<long> Version()
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<FinyteDbContext>().Tenants
                .Where(x => x.Id == seed.TenantId).Select(x => x.FinancialDataVersion).SingleAsync();
        }
        var initial = await Version();
        (await Decide(client, seed.DebitId, "exclude")).EnsureSuccessStatusCode();
        Assert.Equal(12500, (await CashFlow(client)).DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
        Assert.Equal(0, (await CashFlow(client)).DailyCashFlow.Sum(x => x.IncomeMinorUnits));
        var excluded = await Version();
        Assert.True(excluded > initial);
        Assert.Single((await GetReview(client, "excluded")).Items);
        (await Decide(client, seed.DebitId, "exclude")).EnsureSuccessStatusCode();
        Assert.Equal(excluded, await Version());
        (await client.PostAsync("/api/internal-transfers/reclassify", null)).EnsureSuccessStatusCode();
        Assert.Equal(12500, (await CashFlow(client)).DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
        (await Decide(client, seed.DebitId, "reset")).EnsureSuccessStatusCode();
        Assert.Equal(2500, (await CashFlow(client)).DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
        Assert.Empty((await GetReview(client, "excluded")).Items);
        (await Decide(client, seed.OtherId, "mark", seed.CreditAccountId)).EnsureSuccessStatusCode();
        Assert.Equal(0, (await CashFlow(client)).DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
        (await client.PostAsync("/api/internal-transfers/reclassify", null)).EnsureSuccessStatusCode();
        var marked = (await GetReview(client)).Items.Single(x => x.Id == seed.OtherId);
        Assert.Equal("manual", marked.Source);
        Assert.Equal(seed.CreditAccountId, marked.CounterpartyAccountId);
        Assert.Equal(HttpStatusCode.BadRequest, (await Decide(client, seed.OtherId, "mark", seed.DebitAccountId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Decide(client, seed.OtherId, "mark", Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Decide(client, seed.OtherId, "mark")).StatusCode);
    }

    [Fact]
    public async Task ImportingTheOtherAccountClassifiesExistingRows()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        Guid cardId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var card = new Account { TenantId = seed.TenantId, Name = "Card", CreatedAt = DateTimeOffset.UtcNow };
            dbContext.Accounts.Add(card);
            dbContext.Transactions.Add(Transaction(seed.TenantId, seed.DebitAccountId, -300, 20, "Transfer to xx7777 card payment"));
            await dbContext.SaveChangesAsync();
            cardId = card.Id;
        }
        Assert.Equal(32500, (await CashFlow(client)).DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(cardId.ToString()), "accountId");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("<OFX><CCACCTFROM><ACCTID>4444333322227777</CCACCTFROM><BANKTRANLIST><STMTTRN><DTPOSTED>20260820<TRNAMT>300<FITID>pay-1<NAME>Payment received</STMTTRN></BANKTRANLIST></OFX>")), "file", "card.ofx");
        (await client.PostAsync("/api/imports/ofx", form)).EnsureSuccessStatusCode();
        Assert.Equal(2500, (await CashFlow(client)).DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
        var transfers = await GetReview(client);
        Assert.Contains(transfers.Items, x => x.CounterpartyAccountId == cardId && x.Amount == -300);
    }

    [Fact]
    public async Task MarkingARowAddsARuleThatClassifiesSimilarRows()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        Guid mortgageId, firstId, secondId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var mortgage = new Account { TenantId = seed.TenantId, Name = "Home loan", CreatedAt = DateTimeOffset.UtcNow };
            dbContext.Accounts.Add(mortgage);
            var first = Transaction(seed.TenantId, seed.DebitAccountId, -800, 21, "Transfer To J Citizen CommBank App mortgage 1234567");
            var second = Transaction(seed.TenantId, seed.DebitAccountId, -900, 28, "Transfer To J Citizen CommBank App mortgage 7654321");
            dbContext.Transactions.AddRange(first, second);
            await dbContext.SaveChangesAsync();
            (mortgageId, firstId, secondId) = (mortgage.Id, first.Id, second.Id);
        }
        Assert.Equal(172500, (await CashFlow(client)).DailyCashFlow.Sum(x => x.ExpenseMinorUnits));

        (await Decide(client, firstId, "mark", mortgageId)).EnsureSuccessStatusCode();

        Assert.Equal(2500, (await CashFlow(client)).DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
        var rows = (await GetReview(client)).Items;
        Assert.Equal("manual", rows.Single(x => x.Id == firstId).Source);
        Assert.Equal("detected", rows.Single(x => x.Id == secondId).Source);
        Assert.Equal(mortgageId, rows.Single(x => x.Id == secondId).CounterpartyAccountId);
        var account = (await client.GetFromJsonAsync<JsonElement>("/api/accounts"))!.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == mortgageId);
        Assert.Equal(["Transfer To J Citizen CommBank App mortgage"], account.GetProperty("transferNicknames").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Equal(1, account.GetProperty("preferencesVersion").GetInt32());

        (await Decide(client, secondId, "mark", mortgageId)).EnsureSuccessStatusCode();
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>("/api/accounts"))!.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == mortgageId).GetProperty("preferencesVersion").GetInt32());

        (await Decide(client, seed.OtherId, "mark", mortgageId, createRule: false)).EnsureSuccessStatusCode();
        Assert.Equal("manual", (await GetReview(client)).Items.Single(x => x.Id == seed.OtherId).Source);
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>("/api/accounts"))!.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == mortgageId).GetProperty("preferencesVersion").GetInt32());
    }

    [Fact]
    public async Task SavingTransferNicknamesReclassifiesOtherAccountsRows()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        Guid mortgageId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var mortgage = new Account { TenantId = seed.TenantId, Name = "Home loan", CreatedAt = DateTimeOffset.UtcNow };
            dbContext.Accounts.Add(mortgage);
            dbContext.Transactions.Add(Transaction(seed.TenantId, seed.DebitAccountId, -800, 21, "Transfer To J Citizen CommBank App mortgage"));
            await dbContext.SaveChangesAsync();
            mortgageId = mortgage.Id;
        }
        Assert.Equal(82500, (await CashFlow(client)).DailyCashFlow.Sum(x => x.ExpenseMinorUnits));

        var tooMany = await client.PutAsJsonAsync($"/api/accounts/{mortgageId}/preferences", new { transferNicknames = Enumerable.Range(1, 11).Select(x => $"nick {x}").ToArray(), expectedVersion = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        var saved = await client.PutAsJsonAsync($"/api/accounts/{mortgageId}/preferences", new { transferNicknames = new[] { " Mortgage ", "mortgage", "" }, expectedVersion = 0 });
        saved.EnsureSuccessStatusCode();
        Assert.Equal(["Mortgage"], (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transferNicknames").EnumerateArray().Select(x => x.GetString()).ToArray());

        Assert.Equal(2500, (await CashFlow(client)).DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
        Assert.Contains((await GetReview(client)).Items, x => x.CounterpartyAccountId == mortgageId && x.Amount == -800 && x.Source == "detected");

        var cleared = await client.PutAsJsonAsync($"/api/accounts/{mortgageId}/preferences", new { transferNicknames = Array.Empty<string>(), expectedVersion = 1 });
        cleared.EnsureSuccessStatusCode();
        Assert.Equal(82500, (await CashFlow(client)).DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
    }

    [PostgreSqlFact]
    public async Task PostgreSqlReviewFiltersAndPagesInTheDatabase()
    {
        var connection = Environment.GetEnvironmentVariable("FINYTE_TEST_POSTGRES")!;
        var schema = $"transfer_review_{Guid.NewGuid():N}";
        await using var admin = new Npgsql.NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new Npgsql.NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
        {
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            await using var factory = new FinyteApiFactory(new Npgsql.NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString);
            using var client = factory.CreateClient();
            using (var scope = factory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<FinyteDbContext>().Database.MigrateAsync();
            }
            var seed = await Seed(factory, client);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
                foreach (var index in Enumerable.Range(0, 54))
                {
                    var row = Transaction(seed.TenantId, seed.DebitAccountId, -index - 200, 2, "Transfer to xx6486");
                    row.Status = "Posted";
                    db.Transactions.Add(row);
                }
                await db.SaveChangesAsync();
            }
            (await client.PostAsync("/api/internal-transfers/reclassify", null)).EnsureSuccessStatusCode();
            var first = await GetReview(client);
            var second = (await client.GetFromJsonAsync<TransferReviewPage>("/api/internal-transfers?from=2026-08-01&to=2026-09-30&view=transfers&page=2"))!;
            Assert.Equal(56, first.TotalCount);
            Assert.Equal(50, first.Items.Count);
            Assert.Equal(6, second.Items.Count);
            Assert.Empty(first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)));
            Assert.Equal(2500, (await CashFlow(client)).DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
        }
        finally
        {
            await using var drop = new Npgsql.NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task OtherFamilyCannotSeeOrReviewTransfersAndBillingIsRequired()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        using var otherClient = factory.CreateClient();
        otherClient.DefaultRequestHeaders.Add("X-Dev-Organization", "org_other-transfers");
        otherClient.DefaultRequestHeaders.Add("X-Dev-User", "other-user");
        await Seed(factory, otherClient);
        Assert.Equal(HttpStatusCode.NotFound, (await Decide(otherClient, seed.DebitId, "exclude")).StatusCode);
        Assert.DoesNotContain((await GetReview(otherClient)).Items, x => x.Id == seed.DebitId);
        using var unpaidClient = factory.CreateClient();
        unpaidClient.DefaultRequestHeaders.Add("X-Dev-Organization", "org_unpaid-transfers");
        await unpaidClient.PostAsJsonAsync("/api/auth/family", new { name = "Unpaid" });
        Assert.Equal(HttpStatusCode.PaymentRequired, (await unpaidClient.GetAsync("/api/internal-transfers")).StatusCode);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await Decide(unpaidClient, seed.DebitId, "exclude")).StatusCode);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await unpaidClient.PostAsync("/api/internal-transfers/reclassify", null)).StatusCode);
    }

    [Theory]
    [InlineData("from=2026-09-01&to=2026-08-01")]
    [InlineData("from=2024-01-01&to=2026-09-01")]
    [InlineData("to=0001-01-01")]
    [InlineData("view=bogus")]
    public async Task RejectsInvalidReviewFilters(string query)
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        await Seed(factory, client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/internal-transfers?{query}")).StatusCode);
    }

    [Fact]
    public async Task DashboardComparisonAndLedgerUseTheSameScopeWithoutOverwritingTheDefaultCache()
    {
        await using var factory = new FinyteApiFactory();
        await VerifyDashboardDrilldown(factory);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlDashboardComparisonAndLedgerUseTheSameScope()
    {
        var connection = Environment.GetEnvironmentVariable("FINYTE_TEST_POSTGRES")!;
        var schema = $"dashboard_test_{Guid.NewGuid():N}";
        await using var admin = new Npgsql.NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new Npgsql.NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
        {
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            await using var factory = new FinyteApiFactory(new Npgsql.NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString);
            using (var scope = factory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<FinyteDbContext>().Database.MigrateAsync();
            }
            await VerifyDashboardDrilldown(factory);
        }
        finally
        {
            await using var drop = new Npgsql.NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task VerifyDashboardDrilldown(FinyteApiFactory factory)
    {
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var tagId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var tag = new TransactionTag { Id = tagId, TenantId = seed.TenantId, Name = "Transfers", Color = "#aaaaaa" };
            dbContext.TransactionTags.Add(tag);
            var expense = await dbContext.Transactions.SingleAsync(x => x.TenantId == seed.TenantId && x.Amount == -25);
            expense.TagAssignments.Add(new TransactionTagAssignment { TransactionId = expense.Id, TagId = tagId });
            var pending = Transaction(seed.TenantId, seed.DebitAccountId, -55, 10, "Pending purchase");
            pending.Status = "pending";
            var undated = Transaction(seed.TenantId, seed.DebitAccountId, -75, 10, "Undated purchase");
            undated.PostedAt = null;
            undated.CreatedAt = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);
            var excludedAccount = new Account { TenantId = seed.TenantId, Name = "Excluded", IncludeInAnalyticsOverride = false };
            dbContext.Accounts.Add(excludedAccount);
            dbContext.Transactions.AddRange(pending, undated, Transaction(seed.TenantId, excludedAccount.Id, -200, 10, "Loan fee"));
            await dbContext.SaveChangesAsync();
            var projector = new OverviewProjector(dbContext, TestCalendar.Tenants(dbContext));
            var projectionScope = new OverviewProjectionScope(seed.TenantId, null, "2026-08");
            var normal = await projector.Rebuild(projectionScope, CancellationToken.None);
            var comparison = await projector.ReadIncludingTransfers(projectionScope, CancellationToken.None);
            Assert.Equal(2500, normal.CurrentMonthSpendMinorUnits);
            Assert.Equal(12500, comparison.CurrentMonthSpendMinorUnits);
            Assert.Equal(normal.AccountBalanceMinorUnits, comparison.AccountBalanceMinorUnits);
            var persisted = await dbContext.OverviewProjections.SingleAsync(x => x.MonthKey == "2026-08");
            Assert.Equal(2500, JsonDocument.Parse(persisted.PayloadJson).RootElement.GetProperty("currentMonthSpendMinorUnits").GetInt64());
        }
        const string filters = "from=2026-08-01&to=2026-08-31&postedOnly=true&analyticsOnly=true&direction=debit";
        var normalPage = await client.GetFromJsonAsync<JsonElement>($"/api/transactions?{filters}&internalTransfers=exclude");
        Assert.Equal(1, normalPage.GetProperty("totalCount").GetInt32());
        Assert.Equal(-2500, normalPage.GetProperty("items")[0].GetProperty("amountMinorUnits").GetInt64());
        var comparisonPage = await client.GetFromJsonAsync<JsonElement>($"/api/transactions?{filters}&internalTransfers=include");
        Assert.Equal(2, comparisonPage.GetProperty("totalCount").GetInt32());
        var tagged = await client.GetFromJsonAsync<JsonElement>($"/api/transactions?{filters}&internalTransfers=exclude&tagIds={tagId}");
        Assert.Equal(1, tagged.GetProperty("totalCount").GetInt32());
        var untagged = await client.GetFromJsonAsync<JsonElement>($"/api/transactions?{filters}&internalTransfers=exclude&untagged=true");
        Assert.Equal(0, untagged.GetProperty("totalCount").GetInt32());
        var only = await client.GetFromJsonAsync<JsonElement>($"/api/transactions?{filters}&internalTransfers=only");
        Assert.Equal(1, only.GetProperty("totalCount").GetInt32());
        Assert.True(only.GetProperty("items")[0].GetProperty("isInternalTransfer").GetBoolean());
        var direct = await client.GetFromJsonAsync<JsonElement>($"/api/transactions?{filters}&internalTransfers=exclude&accountId={seed.DebitAccountId}");
        Assert.Equal(1, direct.GetProperty("totalCount").GetInt32());
        (await client.GetAsync("/api/overview?includeInternalTransfers=true")).EnsureSuccessStatusCode();
        (await client.PostAsync("/api/overview/refresh?includeInternalTransfers=true", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/transactions?internalTransfers=bogus")).StatusCode);
    }

    private static async Task<TransferReviewPage> GetReview(HttpClient client, string view = "transfers") =>
        (await client.GetFromJsonAsync<TransferReviewPage>($"/api/internal-transfers?from=2026-08-01&to=2026-09-30&view={view}"))!;

    private static async Task<CashFlowRangeResponse> CashFlow(HttpClient client) =>
        (await client.GetFromJsonAsync<CashFlowRangeResponse>("/api/cash-flow?from=2026-08-01&to=2026-09-30"))!;

    private static Task<HttpResponseMessage> Decide(HttpClient client, Guid transactionId, string action, Guid? counterpartyAccountId = null, bool? createRule = null) =>
        client.PostAsJsonAsync("/api/internal-transfers/review", new TransferDecisionRequest(transactionId, action, counterpartyAccountId, createRule));

    private static Transaction Transaction(Guid tenantId, Guid accountId, decimal amount, int day, string description) => new()
    {
        TenantId = tenantId, AccountId = accountId, Amount = amount, Currency = "AUD", Status = "posted",
        FiskilTransactionId = Guid.NewGuid().ToString(), Description = description, CreatedAt = DateTimeOffset.UtcNow,
        PostedAt = new DateTimeOffset(2026, 8, day, 0, 0, 0, TimeSpan.Zero)
    };

    private static async Task<TransferSeed> Seed(FinyteApiFactory factory, HttpClient client)
    {
        var provision = await client.PostAsJsonAsync("/api/auth/family", new { name = "Transfer family" });
        provision.EnsureSuccessStatusCode();
        var tenantId = (await provision.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var customer = new BillingCustomer { TenantId = tenantId, StripeCustomerId = $"cus_{tenantId:N}", CreatedAt = DateTimeOffset.UtcNow };
        dbContext.BillingCustomers.Add(customer);
        dbContext.BillingSubscriptions.Add(new BillingSubscription
        {
            TenantId = tenantId, BillingCustomer = customer, StripeCustomerId = customer.StripeCustomerId,
            StripeSubscriptionId = $"sub_{tenantId:N}", StripePriceId = "price_test", Status = "active",
            CurrentPeriodEnd = DateTimeOffset.UtcNow.AddDays(30), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        var debitAccount = new Account { TenantId = tenantId, Name = "Everyday", AccountNumber = "12340449", CurrentBalance = 300, CreatedAt = DateTimeOffset.UtcNow };
        var creditAccount = new Account { TenantId = tenantId, Name = "Savings", AccountNumber = "12346486", CurrentBalance = 700, CreatedAt = DateTimeOffset.UtcNow };
        dbContext.Accounts.AddRange(debitAccount, creditAccount);
        var debit = Transaction(tenantId, debitAccount.Id, -100, 31, "Transfer to xx6486 CommBank app");
        var credit = Transaction(tenantId, creditAccount.Id, 100, 1, "Transfer from xx0449 NetBank");
        credit.PostedAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var other = Transaction(tenantId, debitAccount.Id, -25, 15, "Woolworths Card xx6486 Value Date: 14/08/2026");
        dbContext.Transactions.AddRange(debit, credit, other);
        await dbContext.SaveChangesAsync();
        await new InternalTransferService(dbContext, new ProjectionInvalidator(dbContext, TestCalendar.Tenants(dbContext)), TestCalendar.Tenants(dbContext)).Reclassify(tenantId, CancellationToken.None);
        return new TransferSeed(tenantId, debitAccount.Id, creditAccount.Id, debit.Id, credit.Id, other.Id);
    }

    private sealed record TransferSeed(Guid TenantId, Guid DebitAccountId, Guid CreditAccountId, Guid DebitId, Guid CreditId, Guid OtherId);
}
