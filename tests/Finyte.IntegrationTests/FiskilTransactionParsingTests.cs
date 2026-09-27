using System.Net;
using System.Text;
using Finyte.Data.ProviderSync;
using Microsoft.Extensions.Options;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class FiskilTransactionParsingTests
{
    private const string SandboxResponse = """
        {
          "links": {},
          "transactions": [
            {
              "fiskil_id": "bank_tx_e92872f1",
              "institution_id": "88888",
              "account_id": "account-1",
              "amount": "-155.00",
              "currency": "AUD",
              "description": "PAYMENT   TO SYNERGY RETAIL B  8642675413561",
              "execution_date_time": "2026-09-26T23:43:00Z",
              "extended_data": { "service": "" },
              "category": { "primary_category": "RENT_AND_UTILITIES", "secondary_category": "RENT_AND_UTILITIES_GAS_AND_ELECTRICITY", "confidence_level": "VERY_HIGH" },
              "is_detail_available": false,
              "merchant_category_code": "4900",
              "merchant_name": "Synergy",
              "posting_date_time": "2026-09-26T23:43:00Z",
              "reference": "617456715",
              "status": "POSTED",
              "transaction_id": "wh-loK3REVFy6ZQ==",
              "type": "PAYMENT"
            },
            {
              "fiskil_id": "bank_tx_534e610f",
              "account_id": "account-2",
              "amount": "-842.00",
              "currency": "AUD",
              "description": "Pending ALLIANZ HOME INS AUS, Direct Debit",
              "execution_date_time": "2026-09-26T19:24:00Z",
              "category": { "primary_category": "SERVICES", "secondary_category": "SERVICES_INSURANCE", "confidence_level": "VERY_HIGH" },
              "merchant_category_code": "6300",
              "merchant_name": "Allianz",
              "reference": "354938035",
              "status": "PENDING",
              "transaction_id": "xEv0pfKFEAJy6Z==",
              "type": "DIRECT_DEBIT"
            }
          ]
        }
        """;

    [Fact]
    public async Task ReadsTheFieldsTheTransactionsApiReturns()
    {
        var transactions = (await Client(SandboxResponse).GetTransactions("end-user", null, CancellationToken.None)).ToList();

        var payment = transactions[0];
        Assert.Equal("bank_tx_e92872f1", payment.Id);
        Assert.Equal("account-1", payment.AccountId);
        Assert.Equal(-155.00m, payment.Amount);
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 23, 43, 0, TimeSpan.Zero), payment.PostedAt);
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 23, 43, 0, TimeSpan.Zero), payment.ExecutedAt);
        Assert.Equal("RENT_AND_UTILITIES", payment.PrimaryCategory);
        Assert.Equal("RENT_AND_UTILITIES_GAS_AND_ELECTRICITY", payment.SecondaryCategory);
        Assert.Equal("VERY_HIGH", payment.CategoryConfidence);
        Assert.Equal("4900", payment.MerchantCategoryCode);
        Assert.Equal("PAYMENT", payment.PaymentType);
        Assert.Equal("Synergy", payment.MerchantName);
        Assert.Equal("617456715", payment.Reference);
        Assert.Equal("POSTED", payment.Status);

        var pending = transactions[1];
        Assert.Null(pending.PostedAt);
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 19, 24, 0, TimeSpan.Zero), pending.ExecutedAt);
        Assert.Equal("DIRECT_DEBIT", pending.PaymentType);
        Assert.Equal("PENDING", pending.Status);
    }

    [Fact]
    public async Task StillReadsTheOlderFlatFieldNames()
    {
        const string legacy = """
            { "transactions": [ { "id": "transaction-1", "account_id": "account-1", "amount": -25.5, "posted_at": "2026-06-08T10:00:00Z",
              "primary_category": "FOOD", "secondary_category": "FOOD_COFFEE" } ] }
            """;
        var transaction = Assert.Single(await Client(legacy).GetTransactions("end-user", null, CancellationToken.None));
        Assert.Equal("transaction-1", transaction.Id);
        Assert.Equal(new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero), transaction.PostedAt);
        Assert.Equal("FOOD", transaction.PrimaryCategory);
        Assert.Equal("FOOD_COFFEE", transaction.SecondaryCategory);
    }

    private static FiskilBankingClient Client(string json) =>
        new(new HttpClient(new JsonHandler(json)), Options.Create(new FiskilOptions { BaseUrl = "https://fiskil.test" }), new StaticToken());

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    private sealed class StaticToken : IFiskilAccessTokenProvider
    {
        public Task<string> GetAccessToken(CancellationToken cancellationToken) => Task.FromResult("token");
    }
}
