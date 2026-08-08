using System.Net;
using System.Net.Http.Json;
using Finyte.Core.Accounts;
using Finyte.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class MerchantTagRuleApiTests
{
    [Fact]
    public async Task CreateMerchantRuleAppliesToExistingMatchingTransactions()
    {
        await using var factory = new FinyteApiFactory();
        var client = factory.CreateClient();
        var tenantId = await ResolveTenantId(client);
        var otherTenantId = Guid.NewGuid();
        var tenantAccountId = Guid.NewGuid();
        var otherTenantAccountId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            dbContext.Accounts.AddRange(
                new Account
                {
                    Id = tenantAccountId,
                    TenantId = tenantId,
                    Name = "Everyday",
                    CurrentBalance = 100,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new Account
                {
                    Id = otherTenantAccountId,
                    TenantId = otherTenantId,
                    Name = "Other everyday",
                    CurrentBalance = 100,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            dbContext.Transactions.AddRange(
                new Transaction
                {
                    TenantId = tenantId,
                    AccountId = tenantAccountId,
                    FiskilTransactionId = "tenant-coles-description",
                    Description = "COLES BRISBANE",
                    MerchantName = null,
                    Amount = -42,
                    PostedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new Transaction
                {
                    TenantId = tenantId,
                    AccountId = tenantAccountId,
                    FiskilTransactionId = "tenant-coles-store",
                    Description = "Coles 4568 Ascot AU",
                    MerchantName = "COLES 4568 ASCOT AU",
                    Amount = -16.25m,
                    PostedAt = new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero),
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new Transaction
                {
                    TenantId = otherTenantId,
                    AccountId = otherTenantAccountId,
                    FiskilTransactionId = "other-tenant-coles",
                    Description = "COLES BRISBANE",
                    MerchantName = null,
                    Amount = -39,
                    PostedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                    CreatedAt = DateTimeOffset.UtcNow
                });

            await dbContext.SaveChangesAsync();
        }

        var tagResponse = await client.PostAsJsonAsync("/api/tags", new
        {
            name = "Groceries",
            color = "#22c55e"
        });
        Assert.Equal(HttpStatusCode.Created, tagResponse.StatusCode);
        var tag = await tagResponse.Content.ReadFromJsonAsync<TransactionTagResponse>();
        Assert.NotNull(tag);

        var ruleResponse = await client.PostAsJsonAsync("/api/merchant-tags", new
        {
            merchantName = "Coles",
            tagId = tag.Id
        });
        Assert.Equal(HttpStatusCode.Created, ruleResponse.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var tenantTransaction = await dbContext.Transactions.FirstAsync(x => x.TenantId == tenantId && x.FiskilTransactionId == "tenant-coles-description");
            var tenantStoreTransaction = await dbContext.Transactions.FirstAsync(x => x.TenantId == tenantId && x.FiskilTransactionId == "tenant-coles-store");
            var otherTenantTransaction = await dbContext.Transactions.FirstAsync(x => x.TenantId == otherTenantId && x.FiskilTransactionId == "other-tenant-coles");

            Assert.True(await dbContext.TransactionTagAssignments.AnyAsync(x => x.TransactionId == tenantTransaction.Id && x.TagId == tag.Id));
            Assert.True(await dbContext.TransactionTagAssignments.AnyAsync(x => x.TransactionId == tenantStoreTransaction.Id && x.TagId == tag.Id));
            Assert.False(await dbContext.TransactionTagAssignments.AnyAsync(x => x.TransactionId == otherTenantTransaction.Id));
        }
    }

    private static async Task<Guid> ResolveTenantId(HttpClient client)
    {
        var currentUser = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me");

        Assert.NotNull(currentUser);
        return currentUser.TenantId;
    }

    private sealed record CurrentUserResponse(string UserId, Guid TenantId, string Role);

    private sealed record TransactionTagResponse(Guid Id, string Name, string Color);
}
