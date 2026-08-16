using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Finyte.Data.ProviderSync;

public interface IFiskilLinkClient
{
    Task<string> CreateEndUser(string name, string email, string phone, CancellationToken cancellationToken);
    Task<FiskilAuthSession> CreateAuthSession(string endUserId, CancellationToken cancellationToken);
    Task<FiskilConsent?> GetActiveConsent(string endUserId, string consentId, CancellationToken cancellationToken);
    Task RevokeConsent(string consentId, CancellationToken cancellationToken);
}

public sealed record FiskilAuthSession(string SessionId, DateTimeOffset ExpiresAt);

public sealed record FiskilConsent(string ConsentId, string InstitutionId);

public sealed class FiskilLinkClient(
    HttpClient httpClient,
    IOptions<FiskilOptions> options,
    IFiskilAccessTokenProvider accessTokenProvider) : IFiskilLinkClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> CreateEndUser(string name, string email, string phone, CancellationToken cancellationToken)
    {
        using var request = await CreateRequest(HttpMethod.Post, "/v1/end-users", cancellationToken);
        request.Content = JsonContent.Create(new CreateEndUserRequest(name, email, phone), options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreateEndUserResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Fiskil returned an empty end-user response.");

        return result.EndUserId;
    }

    public async Task<FiskilAuthSession> CreateAuthSession(string endUserId, CancellationToken cancellationToken)
    {
        using var request = await CreateRequest(HttpMethod.Post, "/v1/auth/session", cancellationToken);
        request.Content = JsonContent.Create(new CreateAuthSessionRequest(endUserId, "", ""), options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreateAuthSessionResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Fiskil returned an empty auth-session response.");

        return new FiskilAuthSession(result.SessionId, ParseExpiry(result.ExpiresAt));
    }

    public async Task<FiskilConsent?> GetActiveConsent(string endUserId, string consentId, CancellationToken cancellationToken)
    {
        var path = $"/v1/consent?end_user_id={Uri.EscapeDataString(endUserId)}&active=true";
        using var request = await CreateRequest(HttpMethod.Get, path, cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var consents = await response.Content.ReadFromJsonAsync<IReadOnlyList<ConsentResponse>>(JsonOptions, cancellationToken) ?? [];
        var consent = consents.SingleOrDefault(x => x.Active && x.ArrangementId == consentId);

        return consent is null ? null : new FiskilConsent(consent.ArrangementId, consent.InstitutionId);
    }

    public async Task RevokeConsent(string consentId, CancellationToken cancellationToken)
    {
        using var request = await CreateRequest(
            HttpMethod.Delete,
            $"/v1/consent/{Uri.EscapeDataString(consentId)}",
            cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpRequestMessage> CreateRequest(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        var fiskilOptions = options.Value;
        httpClient.BaseAddress ??= new Uri(fiskilOptions.BaseUrl);
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await accessTokenProvider.GetAccessToken(cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static DateTimeOffset ParseExpiry(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var unixSeconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }

        if (value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var expiry))
        {
            return expiry;
        }

        throw new InvalidOperationException("Fiskil returned an invalid auth-session expiry.");
    }

    private sealed record CreateEndUserRequest(string Name, string Email, string Phone);

    private sealed record CreateEndUserResponse([property: JsonPropertyName("end_user_id")] string EndUserId);

    private sealed record CreateAuthSessionRequest(
        [property: JsonPropertyName("end_user_id")] string EndUserId,
        [property: JsonPropertyName("redirect_uri")] string RedirectUri,
        [property: JsonPropertyName("cancel_uri")] string CancelUri);

    private sealed record CreateAuthSessionResponse(
        [property: JsonPropertyName("session_id")] string SessionId,
        [property: JsonPropertyName("expires_at")] JsonElement ExpiresAt);

    private sealed record ConsentResponse(
        [property: JsonPropertyName("arrangement_id")] string ArrangementId,
        [property: JsonPropertyName("institution_id")] string InstitutionId,
        [property: JsonPropertyName("active")] bool Active);
}
