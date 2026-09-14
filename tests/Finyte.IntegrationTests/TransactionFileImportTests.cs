using System.Net;
using System.Net.Http.Json;
using System.Text;
using Finyte.Core.Accounts;
using Finyte.Core.Billing;
using Finyte.Data;
using Finyte.Data.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class TransactionFileImportTests
{
    [Theory]
    [InlineData("20260914120000[10:AEST]", "2026-09-14T02:00:00Z")]
    [InlineData("20260914", "2026-09-14T00:00:00Z")]
    public void ParsesDatedBalances(string date, string expected)
    {
        var balance = TransactionFileParser.ParseBalance($"<LEDGERBAL><BALAMT>-12.34<DTASOF>{date}</LEDGERBAL>");
        Assert.Equal(-12.34m, balance!.CurrentBalance);
        Assert.Equal(DateTimeOffset.Parse(expected), balance.AsOf);
        Assert.Null(TransactionFileParser.ParseBalance(Wrap(Row("one"))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BalanceSnapshotsRespectProviderOwnershipAndDateEvenWithDuplicateTransactions(bool providerManaged)
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var (tenantId, accountId) = await Setup(factory, client);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var account = await dbContext.Accounts.SingleAsync();
            account.FiskilAccountId = providerManaged ? "bank-account" : null;
            account.BalanceAsOf = account.CreatedAt; // Legacy placeholder must not block an older export.
            await dbContext.SaveChangesAsync();
        }
        foreach (var snapshot in new[] { ("20260901", "100"), ("20260903", "200"), ("20260902", "150") })
        {
            var content = Wrap(Row("one")) + $"<LEDGERBAL><BALAMT>{snapshot.Item2}<DTASOF>{snapshot.Item1}</LEDGERBAL>";
            (await Upload(client, accountId, content)).EnsureSuccessStatusCode();
        }
        (await Upload(client, accountId, Wrap(Row("one")))).EnsureSuccessStatusCode();
        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var result = await verificationDb.Accounts.SingleAsync();
        Assert.Equal(providerManaged ? 500m : 200m, result.CurrentBalance);
        Assert.Equal(providerManaged ? result.CreatedAt : new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero), result.BalanceAsOf);
        Assert.Equal(1, await verificationDb.Transactions.CountAsync());
        Assert.Equal(providerManaged ? 1 : 2, (await verificationDb.Tenants.SingleAsync(x => x.Id == tenantId)).FinancialDataVersion);
    }

    [Fact]
    public async Task NewImportAccountHasNoBalanceAndIgnoresManualAmounts()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        await Setup(factory, client);
        var response = await client.PostAsJsonAsync("/api/accounts", new { name = "New import account", currency = "AUD", currentBalance = 999 });
        response.EnsureSuccessStatusCode();
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, json.RootElement.GetProperty("balanceAsOf").ValueKind);
        Assert.Equal(0m, json.RootElement.GetProperty("currentBalance").GetDecimal());
    }

    [Theory]
    [InlineData("<STMTTRN><DTPOSTED>20260823<TRNAMT>-12.34<FITID>one<NAME>Coffee &amp; Co</STMTTRN>")]
    [InlineData("<STMTTRN><DTPOSTED>20260823120000[10:AEST]</DTPOSTED><TRNAMT>-12.34</TRNAMT><FITID>one</FITID><NAME>Coffee &amp; Co</NAME></STMTTRN>")]
    public void ParsesSgmlAndXmlExports(string row)
    {
        var transaction = Assert.Single(TransactionFileParser.Parse("export.OFX", Wrap(row)));
        Assert.Equal(new DateOnly(2026, 8, 23), transaction.PostedDate);
        Assert.Equal(-1234, transaction.AmountMinorUnits);
        Assert.Equal("Coffee & Co", transaction.Description);
    }

    [Theory]
    [InlineData("<STMTTRN><DTPOSTED>20260230<TRNAMT>1</STMTTRN>")]
    [InlineData("<STMTTRN><DTPOSTED>20260823<TRNAMT>oops</STMTTRN>")]
    [InlineData("<STMTTRN><DTPOSTED>20260823</STMTTRN>")]
    [InlineData("")]
    public void RejectsInvalidTransactions(string row)
    {
        Assert.Throws<InvalidDataException>(() => TransactionFileParser.Parse("export.ofx", Wrap(row)));
    }

    [Fact]
    public async Task ReimportSkipsDuplicatesAndRefreshesProjectionsWithMerchantTags()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var (tenantId, accountId) = await Setup(factory, client);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var tag = new TransactionTag { TenantId = tenantId, Name = "Coffee", Color = "#aaaaaa", CreatedAt = DateTimeOffset.UtcNow };
            dbContext.TransactionTags.Add(tag);
            dbContext.MerchantTagRules.Add(new MerchantTagRule { TenantId = tenantId, MerchantName = "Coffee", MerchantKey = "coffee", TagId = tag.Id, CreatedAt = DateTimeOffset.UtcNow });
            await dbContext.SaveChangesAsync();
        }
        var content = Wrap(Row("one") + Row("two"));
        var first = await Upload(client, accountId, content);
        var second = await Upload(client, accountId, content);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(2, (await first.Content.ReadFromJsonAsync<ImportResult>())!.ImportedCount);
        var repeat = (await second.Content.ReadFromJsonAsync<ImportResult>())!;
        Assert.Equal(0, repeat.ImportedCount);
        Assert.Equal(2, repeat.SkippedCount);

        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        Assert.Equal(2, await verificationDb.Transactions.CountAsync());
        Assert.Equal(2, await verificationDb.TransactionTagAssignments.CountAsync());
        Assert.Equal(2, await verificationDb.TransactionFileIdentities.CountAsync());
        Assert.Equal(2, await verificationDb.TransactionFileImports.CountAsync());
        Assert.Equal(1, (await verificationDb.Tenants.SingleAsync(x => x.Id == tenantId)).FinancialDataVersion);
        Assert.Equal(500m, (await verificationDb.Accounts.SingleAsync()).CurrentBalance);
    }

    [Fact]
    public async Task MissingBankIdsPreserveRepeatedOccurrencesAndDistinctBankIdsStayDistinct()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var (_, accountId) = await Setup(factory, client);
        var content = Wrap(Row(null) + Row(null));
        Assert.Equal(2, (await (await Upload(client, accountId, content)).Content.ReadFromJsonAsync<ImportResult>())!.ImportedCount);
        Assert.Equal(2, (await (await Upload(client, accountId, content)).Content.ReadFromJsonAsync<ImportResult>())!.SkippedCount);
        Assert.Equal(1, (await (await Upload(client, accountId, Wrap(Row("different-id")))).Content.ReadFromJsonAsync<ImportResult>())!.ImportedCount);
    }

    [Fact]
    public async Task LinksLegacyProviderTransactionsAndReservesKnownMatches()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var (tenantId, accountId) = await Setup(factory, client);
        var legacyId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            dbContext.Transactions.Add(new Transaction
            {
                Id = legacyId, TenantId = tenantId, AccountId = accountId, FiskilTransactionId = "provider-one",
                Description = "Card purchase Coffee Brisbane", Amount = -12.34m,
                PostedAt = new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero), CreatedAt = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }
        var first = (await (await Upload(client, accountId, Wrap(Row("one")))).Content.ReadFromJsonAsync<ImportResult>())!;
        Assert.Equal(0, first.ImportedCount);
        Assert.Equal(1, first.SkippedCount);
        var second = (await (await Upload(client, accountId, Wrap(Row("two") + Row("one")))).Content.ReadFromJsonAsync<ImportResult>())!;
        Assert.Equal(1, second.ImportedCount);
        Assert.Equal(1, second.SkippedCount);
        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        Assert.Equal("provider-one", (await verificationDb.Transactions.SingleAsync(x => x.Id == legacyId)).FiskilTransactionId);
        Assert.Equal(2, await verificationDb.Transactions.CountAsync());
    }

    [Theory]
    [InlineData("<OFX><BANKTRANLIST><STMTTRN><DTPOSTED>bad<TRNAMT>1</STMTTRN></BANKTRANLIST></OFX>")]
    [InlineData("<OFX><CURDEF>USD<BANKTRANLIST><STMTTRN><DTPOSTED>20260823<TRNAMT>1</STMTTRN></BANKTRANLIST></OFX>")]
    [InlineData("<OFX><BANKTRANLIST></BANKTRANLIST><BANKTRANLIST></BANKTRANLIST></OFX>")]
    [InlineData("<OFX><BANKTRANLIST><STMTTRN><DTPOSTED>20260823<TRNAMT>1</STMTTRN><STMTTRN><DTPOSTED>bad<TRNAMT>2</STMTTRN></BANKTRANLIST></OFX>")]
    public async Task InvalidFilesLeaveNoTransactionsAndRecordFailure(string content)
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var (_, accountId) = await Setup(factory, client);
        var response = await Upload(client, accountId, content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        Assert.Empty(await dbContext.Transactions.ToListAsync());
        Assert.Empty(await dbContext.TransactionFileIdentities.ToListAsync());
        Assert.Equal("failed", (await dbContext.TransactionFileImports.SingleAsync()).Status);
    }

    [Fact]
    public async Task RejectsOtherFamilyAccountAndKeepsHistoryPrivate()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var (_, accountId) = await Setup(factory, client);
        (await Upload(client, accountId, Wrap(Row("one")))).EnsureSuccessStatusCode();
        using var otherClient = factory.CreateClient();
        otherClient.DefaultRequestHeaders.Add("X-Dev-Organization", "org_other-family");
        otherClient.DefaultRequestHeaders.Add("X-Dev-User", "other-user");
        await Setup(factory, otherClient);
        Assert.Equal(HttpStatusCode.NotFound, (await Upload(otherClient, accountId, Wrap(Row("one")))).StatusCode);
        Assert.Empty((await otherClient.GetFromJsonAsync<List<ImportResult>>("/api/imports"))!);
    }

    [Fact]
    public async Task ImportRequiresBillingAccess()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/family", new { name = "Test family" });
        Assert.Equal(HttpStatusCode.PaymentRequired, (await Upload(client, Guid.NewGuid(), Wrap(Row("one")))).StatusCode);
    }

    [Fact]
    public async Task DuplicateBankIdsWithinAnUploadAreSkipped()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var (_, accountId) = await Setup(factory, client);
        var response = await Upload(client, accountId, Wrap(Row("one") + Row("one")));
        var result = (await response.Content.ReadFromJsonAsync<ImportResult>())!;
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.SkippedCount);
    }

    private static async Task<(Guid TenantId, Guid AccountId)> Setup(FinyteApiFactory factory, HttpClient client)
    {
        var provision = await client.PostAsJsonAsync("/api/auth/family", new { name = "Import family" });
        provision.EnsureSuccessStatusCode();
        var tenantId = (await provision.Content.ReadFromJsonAsync<CurrentUser>())!.TenantId;
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
        var account = new Account { TenantId = tenantId, Name = "Everyday", CurrentBalance = 500m, CreatedAt = DateTimeOffset.UtcNow };
        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync();
        return (tenantId, account.Id);
    }

    private static async Task<HttpResponseMessage> Upload(HttpClient client, Guid accountId, string content)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(accountId.ToString()), "accountId");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", "transactions.ofx");
        return await client.PostAsync("/api/imports/ofx", form);
    }

    private static string Wrap(string rows) => $"<OFX><BANKTRANLIST>{rows}</BANKTRANLIST></OFX>";
    private static string Row(string? bankId) => $"<STMTTRN><DTPOSTED>20260823<TRNAMT>-12.34{(bankId is null ? "" : $"<FITID>{bankId}")}<NAME>Coffee</STMTTRN>";
    private sealed record CurrentUser(Guid TenantId);
    private sealed record ImportResult(int ImportedCount, int SkippedCount, int TotalCount);
}
