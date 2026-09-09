using System.Net;
using System.Net.Http.Json;
using Finyte.Core.Accounts;
using Finyte.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class AutomaticTaggingApiTests
{
    [Fact]
    public async Task LegacyMatchingWordsAreVisibleAndChangeOnlyWhenTheRuleIsEdited()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var setup = await Setup(factory, client);
        var rule = await CreateRule(client, setup.TagId, "Coffee");
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var stored = await dbContext.MerchantTagRules.SingleAsync(x => x.Id == rule.Id);
            stored.MerchantName = "Coffee Melbourne";
            await dbContext.SaveChangesAsync();
        }
        var listed = Assert.Single((await client.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/merchant-tags"))!);
        Assert.True(listed.GetProperty("usesLegacyMatchingWords").GetBoolean());
        Assert.Equal("coffee", listed.GetProperty("matchingWords").GetString());
        (await client.PutAsJsonAsync($"/api/merchant-tags/{rule.Id}", new { merchantName = "Coffee Melbourne", tagId = setup.TagId })).EnsureSuccessStatusCode();
        listed = Assert.Single((await client.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/merchant-tags"))!);
        Assert.False(listed.GetProperty("usesLegacyMatchingWords").GetBoolean());
        Assert.Equal("coffee melbourne", listed.GetProperty("matchingWords").GetString());
        using var verification = factory.Services.CreateScope();
        Assert.Empty(await verification.ServiceProvider.GetRequiredService<FinyteDbContext>().TransactionTagAssignments.ToListAsync());
    }

    [Fact]
    public async Task OverlappingRulesUseRemainingSupportAndManualPromotionIsExplicit()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var setup = await Setup(factory, client);
        var broad = await CreateRule(client, setup.TagId, "Coffee");
        var specific = await CreateRule(client, setup.TagId, "Coffee House");
        var tags = await SetTags(client, setup.TransactionId, [setup.TagId]);
        Assert.Equal("merchant-rule", Assert.Single(tags).Source);
        Assert.Equal(specific.Id, tags[0].MerchantRuleId);

        (await client.DeleteAsync($"/api/merchant-tags/{specific.Id}")).EnsureSuccessStatusCode();
        tags = await SetTags(client, setup.TransactionId, [setup.TagId]);
        Assert.Equal(broad.Id, Assert.Single(tags).MerchantRuleId);
        tags = await SetTags(client, setup.TransactionId, [setup.TagId], [setup.TagId]);
        Assert.Equal("manual", Assert.Single(tags).Source);
        Assert.Null(tags[0].MerchantRuleId);
        (await client.DeleteAsync($"/api/merchant-tags/{broad.Id}")).EnsureSuccessStatusCode();
        Assert.Equal("manual", Assert.Single(await SetTags(client, setup.TransactionId, [setup.TagId])).Source);
    }

    [Fact]
    public async Task RemovedTagsStayRemovedAfterRuleEditsAndReturnOnlyWhenRestored()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var setup = await Setup(factory, client);
        var rule = await CreateRule(client, setup.TagId, "Coffee");
        Assert.Empty(await SetTags(client, setup.TransactionId, []));
        (await client.PutAsJsonAsync($"/api/merchant-tags/{rule.Id}", new { merchantName = "Coffee House", tagId = setup.TagId })).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            Assert.Empty(await dbContext.TransactionTagAssignments.Where(x => x.TransactionId == setup.TransactionId).ToListAsync());
            Assert.Single(await dbContext.TransactionTagExclusions.Where(x => x.TransactionId == setup.TransactionId).ToListAsync());
        }
        var response = await client.PostAsync($"/api/transactions/{setup.TransactionId}/tags/restore-automatic", null);
        response.EnsureSuccessStatusCode();
        var restored = Assert.Single((await response.Content.ReadFromJsonAsync<List<TagResponse>>())!);
        Assert.Equal("merchant-rule", restored.Source);
        Assert.Equal("Coffee House", restored.MerchantRuleName);
    }

    [Fact]
    public async Task RuleUpdateMovesAutomaticTagsAndDeleteKeepsLegacyAndManualAssignments()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var setup = await Setup(factory, client);
        var legacyId = Guid.NewGuid();
        var manualId = Guid.NewGuid();
        var otherMerchantId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var legacy = CreateTransaction(setup.TenantId, setup.AccountId, legacyId, "Coffee House");
            legacy.TagAssignments.Add(new TransactionTagAssignment { TransactionId = legacy.Id, TagId = setup.TagId });
            var manual = CreateTransaction(setup.TenantId, setup.AccountId, manualId, "Coffee House");
            manual.TagAssignments.Add(new TransactionTagAssignment { TransactionId = manual.Id, TagId = setup.TagId, Source = TransactionTagSource.Manual });
            dbContext.Transactions.AddRange(legacy, manual, CreateTransaction(setup.TenantId, setup.AccountId, otherMerchantId, "Fuel Station"));
            await dbContext.SaveChangesAsync();
        }
        var rule = await CreateRule(client, setup.TagId, "Coffee");
        (await client.PutAsJsonAsync($"/api/merchant-tags/{rule.Id}", new { merchantName = "Fuel", tagId = setup.TagId })).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            Assert.False(await dbContext.TransactionTagAssignments.AnyAsync(x => x.TransactionId == setup.TransactionId));
            Assert.True(await dbContext.TransactionTagAssignments.AnyAsync(x => x.TransactionId == otherMerchantId && x.Source == "merchant-rule"));
        }
        (await client.DeleteAsync($"/api/merchant-tags/{rule.Id}")).EnsureSuccessStatusCode();
        using var verification = factory.Services.CreateScope();
        var database = verification.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var assignments = await database.TransactionTagAssignments.ToListAsync();
        Assert.Equal(2, assignments.Count);
        Assert.Contains(assignments, x => x.TransactionId == legacyId && x.Source == "legacy");
        Assert.Contains(assignments, x => x.TransactionId == manualId && x.Source == "manual");
        Assert.Equal(3, (await database.Tenants.SingleAsync(x => x.Id == setup.TenantId)).FinancialDataVersion);
    }

    [Fact]
    public async Task RuleTagChangeRetractsOldAutomaticAssignment()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var setup = await Setup(factory, client);
        var response = await client.PostAsJsonAsync("/api/tags", new { name = "Eating out", color = "#aaaaaa" });
        var replacement = (await response.Content.ReadFromJsonAsync<TagResponse>())!;
        var rule = await CreateRule(client, setup.TagId, "Coffee");
        (await client.PutAsJsonAsync($"/api/merchant-tags/{rule.Id}", new { merchantName = "Coffee", tagId = replacement.Id })).EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        Assert.Equal(replacement.Id, (await dbContext.TransactionTagAssignments.SingleAsync()).TagId);
    }

    [Fact]
    public async Task AllMutationPathsRejectOtherFamilyRowsAndInvalidManualSelections()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var setup = await Setup(factory, client);
        var rule = await CreateRule(client, setup.TagId, "Coffee");
        using var otherClient = factory.CreateClient();
        otherClient.DefaultRequestHeaders.Add("X-Dev-Organization", "org_other_tagging");
        otherClient.DefaultRequestHeaders.Add("X-Dev-User", "other_tagging_user");
        var other = await Setup(factory, otherClient);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PutAsJsonAsync($"/api/merchant-tags/{rule.Id}", new { merchantName = "Coffee", tagId = other.TagId })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.DeleteAsync($"/api/merchant-tags/{rule.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PostAsync($"/api/transactions/{setup.TransactionId}/tags/restore-automatic", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PutAsJsonAsync($"/api/transactions/{setup.TransactionId}/tags", new { tagIds = new[] { other.TagId } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/merchant-tags", new { merchantName = "Coffee", tagId = other.TagId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/transactions/{setup.TransactionId}/tags", new { tagIds = new[] { other.TagId } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/transactions/{setup.TransactionId}/tags", new { tagIds = Array.Empty<Guid>(), manualTagIds = new[] { setup.TagId } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/transactions/{setup.TransactionId}/tags", new { tagIds = (Guid[]?)null })).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("!@#$")]
    public async Task EmptyNormalizedMerchantRulesAreRejected(string merchantName)
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var setup = await Setup(factory, client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/merchant-tags", new { merchantName, tagId = setup.TagId })).StatusCode);
    }

    private static async Task<SetupData> Setup(FinyteApiFactory factory, HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/family", new { name = "Tagging family" });
        response.EnsureSuccessStatusCode();
        var tenantId = (await response.Content.ReadFromJsonAsync<CurrentUser>())!.TenantId;
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var account = new Account { TenantId = tenantId, Name = "Everyday" };
        var tag = new TransactionTag { TenantId = tenantId, Name = "Coffee", Color = "#aaaaaa" };
        var transaction = CreateTransaction(tenantId, account.Id, Guid.NewGuid(), "Coffee House");
        dbContext.Accounts.Add(account);
        dbContext.TransactionTags.Add(tag);
        dbContext.Transactions.Add(transaction);
        await dbContext.SaveChangesAsync();
        return new SetupData(tenantId, account.Id, tag.Id, transaction.Id);
    }

    private static Transaction CreateTransaction(Guid tenantId, Guid accountId, Guid transactionId, string merchant)
    {
        return new Transaction
        {
            Id = transactionId, TenantId = tenantId, AccountId = accountId, MerchantName = merchant,
            FiskilTransactionId = transactionId.ToString("N"), Amount = -5,
            PostedAt = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero), CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static async Task<RuleResponse> CreateRule(HttpClient client, Guid tagId, string merchantName)
    {
        var response = await client.PostAsJsonAsync("/api/merchant-tags", new { merchantName, tagId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RuleResponse>())!;
    }

    private static async Task<List<TagResponse>> SetTags(HttpClient client, Guid transactionId, Guid[] tagIds, Guid[]? manualTagIds = null)
    {
        var response = await client.PutAsJsonAsync($"/api/transactions/{transactionId}/tags", new { tagIds, manualTagIds });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<TagResponse>>())!;
    }

    private sealed record CurrentUser(Guid TenantId);
    private sealed record SetupData(Guid TenantId, Guid AccountId, Guid TagId, Guid TransactionId);
    private sealed record TagResponse(Guid Id, string Name, string? Source, Guid? MerchantRuleId, string? MerchantRuleName);
    private sealed record RuleResponse(Guid Id);
}
