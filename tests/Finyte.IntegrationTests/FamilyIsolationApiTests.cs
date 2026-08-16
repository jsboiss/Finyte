using System.Net;
using System.Net.Http.Json;
using Finyte.Core.Accounts;
using Finyte.Core.ProviderSync;
using Finyte.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class FamilyIsolationApiTests
{
    [Fact]
    public async Task FamilyMembersShareFamilyDataAndOtherFamilyCannotAccessIt()
    {
        await using var factory = new FinyteApiFactory();
        var ownerClient = CreateClient(factory, "user_owner", "org_family-a", "org:admin");
        var memberClient = CreateClient(factory, "user_member", "org_family-a", "org:member");
        var otherFamilyClient = CreateClient(factory, "user_other", "org_family-b", "org:admin");
        var owner = await Provision(ownerClient, "Family A");
        var member = await Provision(memberClient, "Family A");
        var otherFamily = await Provision(otherFamilyClient, "Family B");
        Assert.Equal(owner.TenantId, member.TenantId);
        Assert.NotEqual(owner.TenantId, otherFamily.TenantId);

        Guid connectionId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var ownerMemberId = await dbContext.TenantMembers
                .Where(x => x.TenantId == owner.TenantId && x.UserId == "user_owner")
                .Select(x => x.Id)
                .SingleAsync();
            var connection = new ProviderConnection
            {
                TenantId = owner.TenantId,
                TenantMemberId = ownerMemberId,
                EndUserId = "end-user-a",
                ConsentId = "consent-a",
                Status = ProviderConnectionStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            connectionId = connection.Id;
            dbContext.ProviderConnections.Add(connection);
            dbContext.Accounts.AddRange(
                new Account
                {
                    TenantId = owner.TenantId,
                    ProviderConnectionId = connection.Id,
                    Name = "Family A account",
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new Account
                {
                    TenantId = otherFamily.TenantId,
                    Name = "Family B account",
                    CreatedAt = DateTimeOffset.UtcNow
                });
            dbContext.ProviderAuthSessions.Add(new ProviderAuthSession
            {
                TenantId = owner.TenantId,
                TenantMemberId = ownerMemberId,
                EndUserId = "end-user-a",
                SessionId = "family-a-session",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                CreatedAt = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        var ownerAccounts = await ownerClient.GetFromJsonAsync<IReadOnlyList<AccountResponse>>("/api/accounts");
        var memberAccounts = await memberClient.GetFromJsonAsync<IReadOnlyList<AccountResponse>>("/api/accounts");
        var otherAccounts = await otherFamilyClient.GetFromJsonAsync<IReadOnlyList<AccountResponse>>("/api/accounts");
        var memberConnections = await memberClient.GetFromJsonAsync<IReadOnlyList<ConnectionResponse>>("/api/provider-connections");
        var otherConnections = await otherFamilyClient.GetFromJsonAsync<IReadOnlyList<ConnectionResponse>>("/api/provider-connections");

        Assert.Equal("Family A account", Assert.Single(ownerAccounts!).Name);
        Assert.Equal("Family A account", Assert.Single(memberAccounts!).Name);
        Assert.Equal("Family B account", Assert.Single(otherAccounts!).Name);
        Assert.Single(memberConnections!);
        Assert.Empty(otherConnections!);

        var crossFamilyDisconnect = await otherFamilyClient.DeleteAsync($"/api/provider-connections/{connectionId}");
        var crossMemberCompletion = await memberClient.PostAsJsonAsync("/api/provider-connections/fiskil/complete", new
        {
            sessionId = "family-a-session",
            consentId = "consent-guessed"
        });

        Assert.Equal(HttpStatusCode.NotFound, crossFamilyDisconnect.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossMemberCompletion.StatusCode);
    }

    private static HttpClient CreateClient(FinyteApiFactory factory, string userId, string organizationId, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Organization", organizationId);
        client.DefaultRequestHeaders.Add("X-Dev-Role", role);
        return client;
    }

    private static async Task<CurrentUserResponse> Provision(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/auth/family", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CurrentUserResponse>())!;
    }

    private sealed record CurrentUserResponse(Guid TenantId);

    private sealed record AccountResponse(Guid Id, string Name);

    private sealed record ConnectionResponse(Guid Id);
}
