using System.Net;
using System.Text;
using Finyte.Data.ProviderSync;
using Microsoft.Extensions.Options;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class FiskilAccessTokenProviderTests
{
    [Fact]
    public async Task AccessTokenIsCachedUntilRefreshWindow()
    {
        var handler = new TokenHandler();
        var timeProvider = new TestTimeProvider(DateTimeOffset.Parse("2026-07-11T00:00:00Z"));
        var provider = new FiskilAccessTokenProvider(
            new HttpClient(handler),
            Options.Create(new FiskilOptions
            {
                BaseUrl = "https://api.fiskil.test",
                ClientId = "client-id",
                ClientSecret = "client-secret"
            }),
            timeProvider);

        var firstToken = await provider.GetAccessToken(CancellationToken.None);
        var cachedToken = await provider.GetAccessToken(CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMinutes(15));
        var refreshedToken = await provider.GetAccessToken(CancellationToken.None);

        Assert.Equal("token-1", firstToken);
        Assert.Equal(firstToken, cachedToken);
        Assert.Equal("token-2", refreshedToken);
        Assert.Equal(2, handler.RequestCount);
    }

    private sealed class TokenHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/token", request.RequestUri!.AbsolutePath);
            Assert.Contains("client-id", requestJson);
            Assert.Contains("client-secret", requestJson);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"token":"token-{{RequestCount}}","expires_in":900}""", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return UtcNow;
        }

        public void Advance(TimeSpan duration)
        {
            UtcNow += duration;
        }
    }
}
