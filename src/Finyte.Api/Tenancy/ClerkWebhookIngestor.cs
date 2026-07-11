using System.Text.Json;
using System.Text.Json.Serialization;
using Finyte.Core.Tenancy;
using Finyte.Data;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Api.Tenancy;

public interface IClerkWebhookIngestor
{
    Task Ingest(string messageId, string payload, CancellationToken cancellationToken);
}

public sealed class ClerkWebhookIngestor(FinyteDbContext dbContext) : IClerkWebhookIngestor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task Ingest(string messageId, string payload, CancellationToken cancellationToken)
    {
        if (await dbContext.ClerkWebhookEvents.AnyAsync(x => x.MessageId == messageId, cancellationToken))
        {
            return;
        }

        var webhook = JsonSerializer.Deserialize<ClerkWebhook>(payload, JsonOptions)
            ?? throw new InvalidOperationException("Clerk webhook payload is empty.");

        switch (webhook.Type)
        {
            case "organization.created":
            case "organization.updated":
                await UpsertOrganization(webhook.Data, cancellationToken);
                break;
            case "organizationMembership.created":
            case "organizationMembership.updated":
                await UpsertMembership(webhook.Data, cancellationToken);
                break;
            case "organizationMembership.deleted":
                await RemoveMembership(webhook.Data, cancellationToken);
                break;
        }

        dbContext.ClerkWebhookEvents.Add(new ClerkWebhookEvent
        {
            MessageId = messageId,
            EventType = webhook.Type,
            ProcessedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Tenant> UpsertOrganization(JsonElement data, CancellationToken cancellationToken)
    {
        var organizationId = GetRequiredString(data, "id");
        var name = GetRequiredString(data, "name");
        var tenant = await dbContext.Tenants
            .SingleOrDefaultAsync(x => x.ClerkOrganizationId == organizationId, cancellationToken);

        if (tenant is null)
        {
            tenant = new Tenant
            {
                ClerkOrganizationId = organizationId,
                Name = name,
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Tenants.Add(tenant);
        }
        else
        {
            tenant.Name = name;
        }

        return tenant;
    }

    private async Task UpsertMembership(JsonElement data, CancellationToken cancellationToken)
    {
        var organization = data.GetProperty("organization");
        var publicUserData = data.GetProperty("public_user_data");
        var tenant = await UpsertOrganization(organization, cancellationToken);
        var userId = GetRequiredString(publicUserData, "user_id");
        var membershipId = GetRequiredString(data, "id");
        var member = await dbContext.TenantMembers
            .SingleOrDefaultAsync(x => x.TenantId == tenant.Id && x.UserId == userId, cancellationToken);

        if (member is null)
        {
            member = new TenantMember
            {
                TenantId = tenant.Id,
                UserId = userId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.TenantMembers.Add(member);
        }

        member.ClerkMembershipId = membershipId;
        member.Role = GetTenantRole(GetRequiredString(data, "role"));
        member.RemovedAt = null;
    }

    private async Task RemoveMembership(JsonElement data, CancellationToken cancellationToken)
    {
        var membershipId = GetRequiredString(data, "id");
        var member = await dbContext.TenantMembers
            .SingleOrDefaultAsync(x => x.ClerkMembershipId == membershipId, cancellationToken);

        if (member is not null)
        {
            member.RemovedAt = DateTimeOffset.UtcNow;
        }
    }

    private static TenantRole GetTenantRole(string role)
    {
        return role == "org:admin" ? TenantRole.Owner : TenantRole.Member;
    }

    private static string GetRequiredString(JsonElement data, string propertyName)
    {
        return data.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? throw new InvalidOperationException($"Clerk webhook property '{propertyName}' is empty.")
            : throw new InvalidOperationException($"Clerk webhook property '{propertyName}' is missing.");
    }

    private sealed record ClerkWebhook(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("data")] JsonElement Data);
}
