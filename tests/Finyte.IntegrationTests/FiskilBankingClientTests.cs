using System.Net;
using Finyte.Data.ProviderSync;
using Microsoft.Extensions.Options;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class FiskilBankingClientTests
{
    [Fact]
    public async Task V1SandboxDataUsesMatchingAccountIdsAndPreservesEnrichment()
    {
        using var httpClient = new HttpClient(new ResponseHandler());
        var client = new FiskilBankingClient(httpClient, Options.Create(new FiskilOptions()), new TokenProvider());

        var account = Assert.Single(await client.GetAccounts("sandbox", CancellationToken.None));
        var transaction = Assert.Single(await client.GetTransactions("sandbox", account.Id, CancellationToken.None));

        Assert.Equal("bank-account", account.Id);
        Assert.Equal(account.Id, transaction.AccountId);
        Assert.Equal("sandbox-consent", account.ConsentId);
        Assert.Equal("bank_tx_1", transaction.Id);
        Assert.Equal(-155m, transaction.Amount);
        Assert.Equal(DateTimeOffset.Parse("2026-09-26T23:43:00Z"), transaction.PostedAt);
        Assert.Equal(transaction.PostedAt, transaction.ExecutedAt);
        Assert.Equal("RENT_AND_UTILITIES", transaction.PrimaryCategory);
        Assert.Equal("RENT_AND_UTILITIES_GAS_AND_ELECTRICITY", transaction.SecondaryCategory);
        Assert.Equal("Synergy", transaction.MerchantName);
        Assert.Contains("VERY_HIGH", transaction.RawJson);
        Assert.Contains("4900", transaction.RawJson);
    }

    private sealed class TokenProvider : IFiskilAccessTokenProvider
    {
        public Task<string> GetAccessToken(CancellationToken cancellationToken) => Task.FromResult("test-token");
    }

    private sealed class ResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = request.RequestUri!.AbsolutePath.EndsWith("/accounts")
                ? """{"accounts":[{"fiskil_id":"bank_account_1","account_id":"bank-account","arrangement_id":"sandbox-consent","institution_id":"88888","display_name":"Full-time Employee Account"}],"links":{}}"""
                : """{"transactions":[{"fiskil_id":"bank_tx_1","account_id":"bank-account","amount":"-155.00","currency":"AUD","posting_date_time":"2026-09-26T23:43:00Z","execution_date_time":"2026-09-26T23:43:00Z","status":"POSTED","merchant_name":"Synergy","merchant_category_code":"4900","category":{"primary_category":"RENT_AND_UTILITIES","secondary_category":"RENT_AND_UTILITIES_GAS_AND_ELECTRICITY","confidence_level":"VERY_HIGH"}}],"links":{}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
        }
    }
}
