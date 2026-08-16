using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class FamilyManagementApiTests
{
    [Fact]
    public async Task DevelopmentOwnerCanInviteAcceptSwitchAndRemoveMember()
    {
        await using var factory = new FinyteApiFactory();
        var ownerClient = CreateClient(factory, "dev-user", "org_dev-family", "org:admin");
        await ownerClient.PostAsJsonAsync("/api/auth/family", new { name = "Dev household" });

        var inviteResponse = await ownerClient.PostAsJsonAsync("/api/family/invitations", new { email = "partner@example.com" });
        var invitation = await inviteResponse.Content.ReadFromJsonAsync<InvitationResponse>();
        Assert.Equal(HttpStatusCode.Created, inviteResponse.StatusCode);
        Assert.NotNull(invitation);

        var acceptResponse = await ownerClient.PostAsync($"/api/family/invitations/{invitation.Id}/dev-accept", null);
        var member = await acceptResponse.Content.ReadFromJsonAsync<MemberResponse>();
        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);
        Assert.NotNull(member);

        var family = await ownerClient.GetFromJsonAsync<FamilyResponse>("/api/family");
        Assert.NotNull(family);
        Assert.Equal(2, family.Members.Count);
        Assert.Empty(family.Invitations);

        var memberClient = CreateClient(factory, member.UserId, family.OrganizationId, "org:member");
        var memberFamily = await memberClient.GetFromJsonAsync<FamilyResponse>("/api/family");
        var forbiddenInvite = await memberClient.PostAsJsonAsync("/api/family/invitations", new { email = "other@example.com" });
        Assert.NotNull(memberFamily);
        Assert.False(memberFamily.CanManage);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenInvite.StatusCode);

        var removeResponse = await ownerClient.DeleteAsync($"/api/family/members/{member.Id}");
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);
        Assert.Single((await ownerClient.GetFromJsonAsync<FamilyResponse>("/api/family"))!.Members);
    }

    [Fact]
    public async Task DevelopmentOwnerCanRevokePendingInvitation()
    {
        await using var factory = new FinyteApiFactory();
        var ownerClient = CreateClient(factory, "dev-user", "org_dev-family", "org:admin");
        await ownerClient.PostAsJsonAsync("/api/auth/family", new { name = "Dev household" });
        var inviteResponse = await ownerClient.PostAsJsonAsync("/api/family/invitations", new { email = "pending@example.com" });
        var invitation = await inviteResponse.Content.ReadFromJsonAsync<InvitationResponse>();

        var revokeResponse = await ownerClient.DeleteAsync($"/api/family/invitations/{invitation!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);
        Assert.Empty((await ownerClient.GetFromJsonAsync<FamilyResponse>("/api/family"))!.Invitations);
    }

    private static HttpClient CreateClient(FinyteApiFactory factory, string userId, string organizationId, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Organization", organizationId);
        client.DefaultRequestHeaders.Add("X-Dev-Role", role);
        return client;
    }

    private sealed record InvitationResponse(Guid Id);

    private sealed record MemberResponse(Guid Id, string UserId);

    private sealed record FamilyResponse(string OrganizationId, bool CanManage, IReadOnlyList<MemberResponse> Members, IReadOnlyList<InvitationResponse> Invitations);
}
