using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Finyte.Api.Tenancy;

public interface IClerkOrganizationClient
{
    Task<string> CreateInvitation(string organizationId, string inviterUserId, string email, CancellationToken cancellationToken);
    Task RevokeInvitation(string organizationId, string invitationId, string requestingUserId, CancellationToken cancellationToken);
    Task RemoveMember(string organizationId, string userId, CancellationToken cancellationToken);
}

public sealed class ClerkOrganizationClient(HttpClient httpClient, IOptions<ClerkWebhookOptions> options) : IClerkOrganizationClient
{
    public async Task<string> CreateInvitation(
        string organizationId,
        string inviterUserId,
        string email,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, $"/v1/organizations/{Uri.EscapeDataString(organizationId)}/invitations");
        request.Content = JsonContent.Create(new CreateInvitationRequest(
            email,
            "org:member",
            inviterUserId,
            string.IsNullOrWhiteSpace(options.Value.InvitationRedirectUrl) ? null : options.Value.InvitationRedirectUrl));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var invitation = await response.Content.ReadFromJsonAsync<InvitationResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Clerk returned an empty invitation response.");
        return invitation.Id;
    }

    public async Task RevokeInvitation(
        string organizationId,
        string invitationId,
        string requestingUserId,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            $"/v1/organizations/{Uri.EscapeDataString(organizationId)}/invitations/{Uri.EscapeDataString(invitationId)}/revoke");
        request.Content = JsonContent.Create(new RevokeInvitationRequest(requestingUserId));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveMember(string organizationId, string userId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Delete,
            $"/v1/organizations/{Uri.EscapeDataString(organizationId)}/memberships/{Uri.EscapeDataString(userId)}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var secretKey = options.Value.SecretKey;
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException("Clerk secret key is not configured.");
        }

        httpClient.BaseAddress ??= new Uri("https://api.clerk.com");
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private sealed record CreateInvitationRequest(
        [property: JsonPropertyName("email_address")] string EmailAddress,
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("inviter_user_id")] string InviterUserId,
        [property: JsonPropertyName("redirect_url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RedirectUrl);

    private sealed record RevokeInvitationRequest(
        [property: JsonPropertyName("requesting_user_id")] string RequestingUserId);

    private sealed record InvitationResponse(string Id);
}
