using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Finyte.Data.ProviderSync;

public interface IFiskilAccessTokenProvider
{
    Task<string> GetAccessToken(CancellationToken cancellationToken);
}

public sealed class FiskilAccessTokenProvider(
    HttpClient httpClient,
    IOptions<FiskilOptions> options,
    TimeProvider timeProvider) : IFiskilAccessTokenProvider
{
    private SemaphoreSlim RefreshLock { get; } = new(1, 1);

    private string? AccessToken { get; set; }

    private DateTimeOffset AccessTokenExpiresAt { get; set; }

    public async Task<string> GetAccessToken(CancellationToken cancellationToken)
    {
        if (HasUsableToken())
        {
            return AccessToken!;
        }

        await RefreshLock.WaitAsync(cancellationToken);
        try
        {
            if (HasUsableToken())
            {
                return AccessToken!;
            }

            var fiskilOptions = options.Value;
            if (string.IsNullOrWhiteSpace(fiskilOptions.ClientId) || string.IsNullOrWhiteSpace(fiskilOptions.ClientSecret))
            {
                throw new InvalidOperationException("Fiskil client credentials are not configured.");
            }

            httpClient.BaseAddress ??= new Uri(fiskilOptions.BaseUrl);
            using var response = await httpClient.PostAsJsonAsync(
                "/v1/token",
                new TokenRequest(fiskilOptions.ClientId, fiskilOptions.ClientSecret),
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Fiskil returned an empty token response.");
            if (string.IsNullOrWhiteSpace(token.Token) || token.ExpiresIn <= 0)
            {
                throw new InvalidOperationException("Fiskil returned an invalid access token.");
            }

            AccessToken = token.Token;
            AccessTokenExpiresAt = timeProvider.GetUtcNow().AddSeconds(Math.Max(token.ExpiresIn - 60, 1));
            return AccessToken;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    private bool HasUsableToken()
    {
        return !string.IsNullOrWhiteSpace(AccessToken) && AccessTokenExpiresAt > timeProvider.GetUtcNow();
    }

    private sealed record TokenRequest(
        [property: JsonPropertyName("client_id")] string ClientId,
        [property: JsonPropertyName("client_secret")] string ClientSecret);

    private sealed record TokenResponse(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
