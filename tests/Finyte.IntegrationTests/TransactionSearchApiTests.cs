using System.Net;
using System.Net.Http.Json;
using Finyte.Core.Accounts;
using Finyte.Core.Billing;
using Finyte.Data;
using Finyte.Data.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class TransactionSearchApiTests
{
    [PostgreSqlFact]
    public async Task PostgreSqlEndpointProjectsDatesAndTagsWhileFilteringBeforePagination()
    {
        var connectionString = Environment.GetEnvironmentVariable("FINYTE_TEST_POSTGRES")!;
        var schema = $"transaction_search_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var createSchema = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection))
        {
            await createSchema.ExecuteNonQueryAsync();
        }

        try
        {
            var isolatedConnection = new NpgsqlConnectionStringBuilder(connectionString)
            {
                SearchPath = schema,
                Pooling = false
            }.ConnectionString;
            await using var factory = new FinyteApiFactory(isolatedConnection);
            using (var scope = factory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<FinyteDbContext>().Database.MigrateAsync();
            }

            var client = factory.CreateClient();
            var seed = await Seed(factory, client);
            var page = await Read(client, $"accountId={seed.AccountId}&search=COFFEE&from=2026-06-01&to=2026-06-30&minAmount=-50&maxAmount=-1&currency=aud&sort=amount&pageSize=1&page=2");
            Assert.Equal(2, page.TotalCount);
            var item = Assert.Single(page.Items);
            Assert.Equal("Morning coffee", item.Description);
            Assert.Equal("2026-06-01", item.PostedDate);
            Assert.Equal(2, item.Tags.Count);

            var dateBounds = await Read(client, "from=2026-06-01&to=2026-06-01&sort=date");
            Assert.Equal(["Morning coffee", "Unposted purchase", "Late dinner"], dateBounds.Items.Select(x => x.Description));
            var tags = await Read(client, $"tagIds={seed.FirstTagId}&tagIds={seed.SecondTagId}&tagMatch=all");
            Assert.Equal("Morning coffee", Assert.Single(tags.Items).Description);
            var literal = await Read(client, $"search={Uri.EscapeDataString("%_\\")}");
            Assert.Equal("Zero adjustment %_\\", Assert.Single(literal.Items).Description);
        }
        finally
        {
            // Only the generated schema created by this test is removed; existing schemas are never touched.
            await using var dropSchema = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
            await dropSchema.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task FiltersAndSortApplyBeforeCountAndPagination()
    {
        await using var factory = new FinyteApiFactory();
        var client = factory.CreateClient();
        var seed = await Seed(factory, client);

        var page = await Read(client, $"accountId={seed.AccountId}&search=coffee&from=2026-06-01&to=2026-06-30&minAmount=-50&maxAmount=-1&currency=aud&sort=amount&pageSize=1&page=2");

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Equal(1, page.PageSize);
        Assert.Equal("Morning coffee", Assert.Single(page.Items).Description);
        Assert.Equal(-500, page.Items[0].AmountMinorUnits);
    }

    [Fact]
    public async Task DateBoundsIncludeTheWholeFinalDayAndUseCreatedDateWhenUnposted()
    {
        await using var factory = new FinyteApiFactory();
        var client = factory.CreateClient();
        await Seed(factory, client);

        var page = await Read(client, "from=2026-06-01&to=2026-06-01&sort=date");

        Assert.Equal(["Morning coffee", "Unposted purchase", "Late dinner"], page.Items.Select(x => x.Description));
        Assert.All(page.Items, x => Assert.Equal("2026-06-01", x.PostedDate));
    }

    [Theory]
    [InlineData("COFFEE", 3)]
    [InlineData("reference-only", 1)]
    [InlineData("%_", 1)]
    [InlineData("\\", 1)]
    [InlineData("none-such", 0)]
    public async Task SearchUsesDescriptionMerchantAndReferenceAsLiteralCaseInsensitiveText(string search, int expectedCount)
    {
        await using var factory = new FinyteApiFactory();
        var client = factory.CreateClient();
        await Seed(factory, client);

        var page = await Read(client, $"search={Uri.EscapeDataString(search)}");

        Assert.Equal(expectedCount, page.TotalCount);
    }

    [Fact]
    public async Task TagFiltersSupportAnyAllRepeatedIdsAndUntagged()
    {
        await using var factory = new FinyteApiFactory();
        var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var tags = $"tagIds={seed.FirstTagId}&tagIds={seed.SecondTagId}";

        var any = await Read(client, tags);
        var all = await Read(client, tags + "&tagMatch=all");
        var repeated = await Read(client, $"tagIds={seed.FirstTagId}&tagIds={seed.FirstTagId}&tagMatch=all");
        var untagged = await Read(client, "untagged=true");

        Assert.Equal(3, any.TotalCount);
        Assert.Equal("Morning coffee", Assert.Single(all.Items).Description);
        Assert.Equal(2, repeated.TotalCount);
        Assert.Equal(4, untagged.TotalCount);
        Assert.All(untagged.Items, x => Assert.Empty(x.Tags));
    }

    [Fact]
    public async Task CombinedFiltersKeepFamilyBoundaryAndUnknownIdsReturnNoResults()
    {
        await using var factory = new FinyteApiFactory();
        var client = factory.CreateClient();
        var seed = await Seed(factory, client);

        var all = await Read(client, "");
        var foreignAccount = await Read(client, $"accountId={seed.ForeignAccountId}");
        var foreignTag = await Read(client, $"tagIds={seed.ForeignTagId}");
        var foreignAndLocalTag = await Read(client, $"tagIds={seed.ForeignTagId}&tagIds={seed.FirstTagId}&tagMatch=all");
        var unknownAccount = await Read(client, $"accountId={Guid.NewGuid()}");

        Assert.Equal(7, all.TotalCount);
        Assert.DoesNotContain(all.Items, x => x.Description == "Foreign coffee");
        Assert.Empty(foreignAccount.Items);
        Assert.Empty(foreignTag.Items);
        Assert.Empty(foreignAndLocalTag.Items);
        Assert.Empty(unknownAccount.Items);
    }

    [Fact]
    public async Task CategoryAndCurrencyFiltersWorkTogetherWithSignedAmounts()
    {
        await using var factory = new FinyteApiFactory();
        var client = factory.CreateClient();
        await Seed(factory, client);

        var page = await Read(client, "category=FOOD&currency=AUD&minAmount=-5&maxAmount=-5");

        Assert.Equal("Morning coffee", Assert.Single(page.Items).Description);
        var zero = await Read(client, "minAmount=0&maxAmount=0");
        Assert.Equal("Zero adjustment %_\\", Assert.Single(zero.Items).Description);
    }

    [Theory]
    [InlineData("date")]
    [InlineData("-date")]
    [InlineData("amount")]
    [InlineData("-amount")]
    [InlineData("description")]
    [InlineData("-description")]
    public async Task EverySortHasStableTiesAcrossPages(string sort)
    {
        await using var factory = new FinyteApiFactory();
        var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var account = await dbContext.Accounts.FindAsync(seed.AccountId);
            Assert.NotNull(account);
            var duplicate = CreateTransaction(account, "Morning coffee", -5m, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
            duplicate.Id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
            dbContext.Transactions.Add(duplicate);
            await dbContext.SaveChangesAsync();
        }

        var first = await Read(client, $"search=Morning%20coffee&sort={sort}&pageSize=1");
        var second = await Read(client, $"search=Morning%20coffee&sort={sort}&pageSize=1&page=2");
        var repeat = await Read(client, $"search=Morning%20coffee&sort={sort}&pageSize=1");

        Assert.Equal(2, first.TotalCount);
        Assert.NotEqual(Assert.Single(first.Items).Id, Assert.Single(second.Items).Id);
        Assert.Equal(first.Items[0].Id, Assert.Single(repeat.Items).Id);
        Assert.Equal(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), second.Items[0].Id);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-2147483648")]
    [InlineData("page=2147483647&pageSize=250")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=251")]
    [InlineData("from=2026-06-02&to=2026-06-01")]
    [InlineData("minAmount=1&maxAmount=-1")]
    [InlineData("minAmount=not-a-number")]
    [InlineData("from=2026-13-01")]
    [InlineData("tagMatch=unknown")]
    [InlineData("sort=invalid")]
    [InlineData("tagIds=not-a-guid")]
    [InlineData("tagIds=00000000-0000-0000-0000-000000000000")]
    [InlineData("tagIds=10000000-0000-0000-0000-000000000001&untagged=true")]
    [InlineData("currency=INVALID")]
    public async Task InvalidRequestsReturnBadRequest(string query)
    {
        await using var factory = new FinyteApiFactory();
        var client = factory.CreateClient();
        await Seed(factory, client);

        var response = await client.GetAsync($"/api/transactions?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MaximumDateDoesNotOverflowAndPageBeyondResultsIsEmpty()
    {
        await using var factory = new FinyteApiFactory();
        var client = factory.CreateClient();
        await Seed(factory, client);

        var maxDate = await Read(client, "to=9999-12-31");
        var emptyPage = await Read(client, "page=10&pageSize=1");
        var tooLong = await client.GetAsync($"/api/transactions?search={new string('a', 201)}");

        Assert.Equal(7, maxDate.TotalCount);
        Assert.Equal(7, emptyPage.TotalCount);
        Assert.Empty(emptyPage.Items);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public void FullSearchTranslatesToPostgreSqlBeforePaging()
    {
        using var dbContext = new FinyteDbContext(new DbContextOptionsBuilder<FinyteDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=unused")
            .Options);
        var filters = new TransactionSearch
        {
            AccountId = Guid.NewGuid(), From = new DateOnly(2026, 6, 1), To = new DateOnly(2026, 6, 30),
            Search = "%_\\", Category = "food", TagIds = [Guid.NewGuid(), Guid.NewGuid()], TagMatch = "all",
            MinAmount = -100m, MaxAmount = 100m, Currency = "AUD", Sort = "amount"
        };

        var sql = filters.Order(filters.Apply(dbContext.Transactions, Guid.NewGuid())).Skip(25).Take(25).ToQueryString();

        Assert.Contains("ORDER BY", sql);
        Assert.Contains("LIMIT", sql);
        Assert.Contains("count(*)", sql);
        Assert.Contains("COALESCE", sql);
        Assert.Contains("TenantId", sql);
    }

    private static async Task<PageResponse> Read(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/transactions?{query}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return (await response.Content.ReadFromJsonAsync<PageResponse>())!;
    }

    private static async Task<SeedResult> Seed(FinyteApiFactory factory, HttpClient client)
    {
        var provision = await client.PostAsJsonAsync("/api/auth/family", new { name = "Search family" });
        var user = (await provision.Content.ReadFromJsonAsync<CurrentUserResponse>())!;
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var now = DateTimeOffset.UtcNow;
        var customer = new BillingCustomer { TenantId = user.TenantId, StripeCustomerId = $"cus_{Guid.NewGuid():N}", CreatedAt = now };
        dbContext.BillingCustomers.Add(customer);
        dbContext.BillingSubscriptions.Add(new BillingSubscription
        {
            TenantId = user.TenantId, BillingCustomer = customer, StripeCustomerId = customer.StripeCustomerId,
            StripeSubscriptionId = $"sub_{Guid.NewGuid():N}", StripePriceId = "price_test", Status = "active",
            CurrentPeriodStart = now.AddDays(-1), CurrentPeriodEnd = now.AddDays(20), CreatedAt = now, UpdatedAt = now
        });
        var account = new Account { TenantId = user.TenantId, Name = "Everyday", Currency = "AUD", CreatedAt = now };
        var otherAccount = new Account { TenantId = user.TenantId, Name = "Savings", Currency = "USD", CreatedAt = now };
        var foreignAccount = new Account { TenantId = Guid.NewGuid(), Name = "Foreign", Currency = "AUD", CreatedAt = now };
        dbContext.Accounts.AddRange(account, otherAccount, foreignAccount);
        var firstTag = new TransactionTag { TenantId = user.TenantId, Name = "Food", Color = "#123456", CreatedAt = now };
        var secondTag = new TransactionTag { TenantId = user.TenantId, Name = "Shared", Color = "#123456", CreatedAt = now };
        var foreignTag = new TransactionTag { TenantId = foreignAccount.TenantId, Name = "Food", Color = "#123456", CreatedAt = now };
        dbContext.TransactionTags.AddRange(firstTag, secondTag, foreignTag);
        var firstDate = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var first = CreateTransaction(account, "Morning coffee", -5m, firstDate);
        first.SecondaryCategory = "Food & drink";
        first.TagAssignments = [Assignment(firstTag), Assignment(secondTag)];
        var second = CreateTransaction(account, "Cafe purchase", -20m, firstDate.AddDays(1));
        second.MerchantName = "COFFEE STORE";
        second.PrimaryCategory = "Food";
        second.TagAssignments = [Assignment(firstTag)];
        var third = CreateTransaction(account, "Late dinner", -80m, firstDate.AddDays(1).AddMilliseconds(-1));
        third.Reference = "Reference-only";
        third.TagAssignments = [Assignment(secondTag)];
        var pending = CreateTransaction(account, "Unposted purchase", -30m, firstDate.AddHours(23).AddMinutes(59));
        pending.CreatedAt = pending.PostedAt!.Value;
        pending.PostedAt = null;
        var foreign = CreateTransaction(foreignAccount, "Foreign coffee", -5m, firstDate);
        foreign.TagAssignments = [Assignment(foreignTag)];
        dbContext.Transactions.AddRange(first, second, third, pending, foreign,
            CreateTransaction(account, "Salary", 100m, firstDate.AddDays(-1)),
            CreateTransaction(otherAccount, "Other coffee", -5m, firstDate.AddDays(5)),
            CreateTransaction(account, "Zero adjustment %_\\", 0m, firstDate.AddDays(3)));
        await dbContext.SaveChangesAsync();
        return new SeedResult(account.Id, firstTag.Id, secondTag.Id, foreignAccount.Id, foreignTag.Id);
    }

    private static TransactionTagAssignment Assignment(TransactionTag tag)
    {
        return new TransactionTagAssignment { Tag = tag, TagId = tag.Id, CreatedAt = DateTimeOffset.UtcNow };
    }

    private static Transaction CreateTransaction(Account account, string description, decimal amount, DateTimeOffset postedAt)
    {
        return new Transaction
        {
            TenantId = account.TenantId, Account = account, FiskilTransactionId = Guid.NewGuid().ToString("N"),
            Description = description, Amount = amount, Currency = account.Currency, PostedAt = postedAt,
            CreatedAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)
        };
    }

    private sealed record SeedResult(Guid AccountId, Guid FirstTagId, Guid SecondTagId, Guid ForeignAccountId, Guid ForeignTagId);
    private sealed record CurrentUserResponse(Guid TenantId);
    private sealed record TagResponse(Guid Id, string Name);
    private sealed record ItemResponse(Guid Id, string Description, long AmountMinorUnits, string PostedDate, IReadOnlyList<TagResponse> Tags);
    private sealed record PageResponse(IReadOnlyList<ItemResponse> Items, int Page, int PageSize, int TotalCount);
}
