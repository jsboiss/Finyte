using System.Net;
using System.Net.Http.Json;
using Finyte.Core.Accounts;
using Finyte.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class MerchantWideTaggingTests
{
    [Fact]
    public async Task AllPaymentsFromAMerchantReplacesItsRulesAndKeepsSinglePaymentChoices()
    {
        await using var factory = new FinyteApiFactory();
        using var http = factory.CreateClient();
        var response = await http.PostAsJsonAsync("/api/auth/family", new { name = "Merchant family" });
        response.EnsureSuccessStatusCode();
        var tenantId = (await response.Content.ReadFromJsonAsync<CurrentUser>())!.TenantId;
        Guid first, second, other, shopping, household, gifts;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var account = new Account { TenantId = tenantId, Name = "Everyday" };
            var tags = new[] { "Shopping", "Household", "Gifts" }.Select(x => new TransactionTag { TenantId = tenantId, Name = x, Color = "#aaaaaa" }).ToList();
            var rows = new[] { ("SHOPCO SPRINGFIELD", 8), ("SHOPCO SPRINGFIELD", 15), ("CAFECO", 16) }.Select(x => Payment(tenantId, account.Id, x.Item1, x.Item2)).ToList();
            dbContext.Accounts.Add(account);
            dbContext.TransactionTags.AddRange(tags);
            dbContext.Transactions.AddRange(rows);
            await dbContext.SaveChangesAsync();
            (first, second, other) = (rows[0].Id, rows[1].Id, rows[2].Id);
            (shopping, household, gifts) = (tags[0].Id, tags[1].Id, tags[2].Id);
        }
        (await http.PostAsJsonAsync("/api/merchant-tags", new { merchantName = "SHOPCO SPRINGFIELD", tagId = shopping })).EnsureSuccessStatusCode();
        (await http.PutAsJsonAsync($"/api/transactions/{second}/tags", new { tagIds = new[] { gifts } })).EnsureSuccessStatusCode();

        var merchant = await http.PutAsJsonAsync($"/api/transactions/{first}/tags/merchant", new { tagIds = new[] { household } });
        merchant.EnsureSuccessStatusCode();
        Assert.Equal("merchant-rule", Assert.Single((await merchant.Content.ReadFromJsonAsync<List<TagResponse>>())!).Source);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var assigned = await db.TransactionTagAssignments.Include(x => x.Tag).ToListAsync();
            Assert.Equal(["Household"], assigned.Where(x => x.TransactionId == first).Select(x => x.Tag!.Name));
            Assert.Equal(["Gifts", "Household"], assigned.Where(x => x.TransactionId == second).Select(x => x.Tag!.Name).Order());
            Assert.DoesNotContain(assigned, x => x.TransactionId == other);
            var rule = Assert.Single(await db.MerchantTagRules.ToListAsync());
            Assert.Equal(household, rule.TagId);
        }

        (await http.PutAsJsonAsync($"/api/transactions/{first}/tags/merchant", new { tagIds = new[] { gifts } })).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            Assert.Equal(gifts, Assert.Single(await scope.ServiceProvider.GetRequiredService<FinyteDbContext>().MerchantTagRules.ToListAsync()).TagId);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PutAsJsonAsync($"/api/transactions/{first}/tags/merchant", new { tagIds = Array.Empty<Guid>() })).StatusCode);
    }

    private static Transaction Payment(Guid tenantId, Guid accountId, string merchant, int day) => new()
    {
        TenantId = tenantId, AccountId = accountId, FiskilTransactionId = Guid.NewGuid().ToString("N"), MerchantName = merchant, Description = merchant,
        Amount = -20, PostedAt = new DateTimeOffset(2026, 9, day, 0, 0, 0, TimeSpan.Zero), CreatedAt = DateTimeOffset.UtcNow
    };

    private sealed record CurrentUser(Guid TenantId);
    private sealed record TagResponse(Guid Id, string Name, string? Source);
}
