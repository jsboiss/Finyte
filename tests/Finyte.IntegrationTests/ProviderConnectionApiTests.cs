using System.Net;
using System.Net.Http.Json;
using Finyte.Core.Billing;
using Finyte.Core.ProviderSync;
using Finyte.Data;
using Finyte.Data.ProviderSync;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class ProviderConnectionApiTests
{
    [Fact]
    public async Task FiskilSessionCreatesMemberOwnedConnectionAndCompletesVerifiedConsent()
    {
        var fiskilClient = new StubFiskilLinkClient();
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = baseFactory.WithWebHostBuilder(x => x.ConfigureServices(services =>
        {
            services.RemoveAll<IFiskilLinkClient>();
            services.AddSingleton<IFiskilLinkClient>(fiskilClient);
        }));
        var client = factory.CreateClient();
        var familyResponse = await client.PostAsJsonAsync("/api/auth/family", new { name = "Test family" });
        var currentUser = await familyResponse.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(currentUser);
        await AddActiveSubscription(factory.Services, currentUser.TenantId);

        var startResponse = await client.PostAsJsonAsync("/api/provider-connections/fiskil/session", new
        {
            name = "Test User",
            email = "test@example.com",
            phone = "+61412345678"
        });
        var session = await startResponse.Content.ReadFromJsonAsync<FiskilSessionResponse>();

        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        Assert.NotNull(session);
        Assert.Equal("session-1", session.SessionId);
        Assert.Equal(1, fiskilClient.CreateEndUserCalls);

        var completeResponse = await client.PostAsJsonAsync("/api/provider-connections/fiskil/complete", new
        {
            sessionId = session.SessionId,
            consentId = "consent-1"
        });

        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var member = await dbContext.TenantMembers.SingleAsync();
        var connection = await dbContext.ProviderConnections.SingleAsync();
        var authSession = await dbContext.ProviderAuthSessions.SingleAsync();
        Assert.Equal(member.Id, connection.TenantMemberId);
        Assert.Equal("end-user-1", connection.EndUserId);
        Assert.Equal("consent-1", connection.ConsentId);
        Assert.Equal("institution-1", connection.InstitutionId);
        Assert.Equal(ProviderConnectionStatus.Active, connection.Status);
        Assert.NotNull(authSession.CompletedAt);
        Assert.Equal(3, await dbContext.ProviderSyncRuns.CountAsync());

        var disconnectResponse = await client.DeleteAsync($"/api/provider-connections/{connection.Id}");

        Assert.Equal(HttpStatusCode.NoContent, disconnectResponse.StatusCode);
        Assert.Equal(1, fiskilClient.RevokeConsentCalls);
        dbContext.ChangeTracker.Clear();
        Assert.Equal(ProviderConnectionStatus.Revoked, (await dbContext.ProviderConnections.SingleAsync()).Status);
        Assert.All(await dbContext.ProviderSyncRuns.ToListAsync(), x => Assert.Equal(ProviderSyncStatus.Cancelled, x.Status));
    }

    private static async Task AddActiveSubscription(IServiceProvider services, Guid tenantId)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var now = DateTimeOffset.UtcNow;
        var customer = new BillingCustomer
        {
            TenantId = tenantId,
            StripeCustomerId = "cus_connection_test",
            CreatedAt = now
        };
        dbContext.BillingCustomers.Add(customer);
        dbContext.BillingSubscriptions.Add(new BillingSubscription
        {
            TenantId = tenantId,
            BillingCustomer = customer,
            StripeSubscriptionId = "sub_connection_test",
            StripeCustomerId = customer.StripeCustomerId,
            StripePriceId = "price_test",
            Status = "active",
            CurrentPeriodStart = now.AddDays(-1),
            CurrentPeriodEnd = now.AddDays(30),
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();
    }

    private sealed class StubFiskilLinkClient : IFiskilLinkClient
    {
        public int CreateEndUserCalls { get; private set; }

        public int RevokeConsentCalls { get; private set; }

        public Task<string> CreateEndUser(string name, string email, string phone, CancellationToken cancellationToken)
        {
            CreateEndUserCalls++;
            return Task.FromResult("end-user-1");
        }

        public Task<FiskilAuthSession> CreateAuthSession(string endUserId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new FiskilAuthSession("session-1", DateTimeOffset.UtcNow.AddHours(1)));
        }

        public Task<FiskilConsent?> GetActiveConsent(string endUserId, string consentId, CancellationToken cancellationToken)
        {
            return Task.FromResult<FiskilConsent?>(new FiskilConsent(consentId, "institution-1"));
        }

        public Task RevokeConsent(string consentId, CancellationToken cancellationToken)
        {
            RevokeConsentCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed record CurrentUserResponse(Guid TenantId);

    private sealed record FiskilSessionResponse(string SessionId, DateTimeOffset ExpiresAt);
}
