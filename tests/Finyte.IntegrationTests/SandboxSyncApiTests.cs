using System.Net;
using System.Net.Http.Json;
using Finyte.Core.ProviderSync;
using Finyte.Data;
using Finyte.Data.ProviderSync;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class SandboxSyncApiTests
{
    [Theory]
    [InlineData("88888", true, HttpStatusCode.Accepted, 3)]
    [InlineData("real-bank", true, HttpStatusCode.BadRequest, 0)]
    [InlineData("88888", false, HttpStatusCode.BadRequest, 0)]
    public async Task OnlyConfirmedSandboxConsentCanQueueNormalSync(string institutionId, bool confirmReset, HttpStatusCode expected, int runCount)
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = baseFactory.WithWebHostBuilder(x =>
        {
            x.UseSetting("Fiskil:Sandbox:Enabled", "true");
            x.UseSetting("Fiskil:Sandbox:EndUserId", "sandbox-user");
            x.UseSetting("Fiskil:Sandbox:ConsentId", "sandbox-consent");
            x.UseSetting("Fiskil:ClientId", "test");
            x.UseSetting("Fiskil:ClientSecret", "test");
            x.ConfigureServices(y =>
            {
                y.RemoveAll<IFiskilLinkClient>();
                y.RemoveAll<IFiskilBankingClient>();
                y.AddSingleton<IFiskilLinkClient>(new LinkClient(institutionId));
                y.AddSingleton<IFiskilBankingClient>(new BankingClient());
            });
        });
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/development/fiskil-sandbox/sync", new { confirmReset });
        Assert.Equal(expected, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        Assert.Equal(runCount, await dbContext.ProviderSyncRuns.CountAsync());
        if (runCount > 0)
        {
            var repeat = await client.PostAsJsonAsync("/api/development/fiskil-sandbox/sync", new { confirmReset = true });
            Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
            Assert.Equal(runCount, await dbContext.ProviderSyncRuns.CountAsync());
            var connection = await dbContext.ProviderConnections.SingleAsync();
            Assert.Equal((await dbContext.TenantMembers.SingleAsync()).Id, connection.TenantMemberId);
        }
    }

    private sealed class LinkClient(string institutionId) : IFiskilLinkClient
    {
        public Task<FiskilConsent?> GetActiveConsent(string endUserId, string consentId, CancellationToken cancellationToken)
            => Task.FromResult<FiskilConsent?>(new FiskilConsent(consentId, institutionId));
        public Task<string> CreateEndUser(string name, string email, string phone, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FiskilAuthSession> CreateAuthSession(string endUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RevokeConsent(string consentId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class BankingClient : IFiskilBankingClient
    {
        public Task<IReadOnlyCollection<FiskilAccountData>> GetAccounts(string endUserId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<FiskilAccountData>>([new("account", null, null, "Sandbox", null, null, "88888", "sandbox-consent", true, "OPEN", null, "{}")]);
        public Task<IReadOnlyCollection<FiskilBalanceData>> GetBalances(string endUserId, string? accountId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<FiskilTransactionData>> GetTransactions(string endUserId, string? accountId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
