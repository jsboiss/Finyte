using Finyte.Api.Tenancy;
using Finyte.Core.Tenancy;
using Finyte.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class ClerkWebhookIngestorTests
{
    [Fact]
    public async Task MembershipCreatedCreatesFamilyAndMember()
    {
        await using var dbContext = CreateDbContext();
        var ingestor = new ClerkWebhookIngestor(dbContext);

        await ingestor.Ingest("message-1", MembershipPayload("organizationMembership.created", "org:admin"), CancellationToken.None);

        var tenant = await dbContext.Tenants.SingleAsync();
        var member = await dbContext.TenantMembers.SingleAsync();
        Assert.Equal("org_family-1", tenant.ClerkOrganizationId);
        Assert.Equal("Test family", tenant.Name);
        Assert.Equal(tenant.Id, member.TenantId);
        Assert.Equal("user_1", member.UserId);
        Assert.Equal("membership_1", member.ClerkMembershipId);
        Assert.Equal(TenantRole.Owner, member.Role);
        Assert.Null(member.RemovedAt);
    }

    [Fact]
    public async Task MembershipUpdatedChangesRoleAndRestoresRemovedMember()
    {
        await using var dbContext = CreateDbContext();
        var tenant = CreateTenant();
        dbContext.Tenants.Add(tenant);
        dbContext.TenantMembers.Add(new TenantMember
        {
            TenantId = tenant.Id,
            UserId = "user_1",
            ClerkMembershipId = "membership_1",
            Role = TenantRole.Owner,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            RemovedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
        var ingestor = new ClerkWebhookIngestor(dbContext);

        await ingestor.Ingest("message-1", MembershipPayload("organizationMembership.updated", "org:member"), CancellationToken.None);

        var member = await dbContext.TenantMembers.SingleAsync();
        Assert.Equal(TenantRole.Member, member.Role);
        Assert.Null(member.RemovedAt);
    }

    [Fact]
    public async Task MembershipDeletedSoftRemovesMemberAndPreservesOwnershipHistory()
    {
        await using var dbContext = CreateDbContext();
        var tenant = CreateTenant();
        var member = new TenantMember
        {
            TenantId = tenant.Id,
            UserId = "user_1",
            ClerkMembershipId = "membership_1",
            Role = TenantRole.Member,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        dbContext.Tenants.Add(tenant);
        dbContext.TenantMembers.Add(member);
        await dbContext.SaveChangesAsync();
        var ingestor = new ClerkWebhookIngestor(dbContext);

        await ingestor.Ingest("message-1", """{"type":"organizationMembership.deleted","data":{"id":"membership_1"}}""", CancellationToken.None);

        Assert.NotNull((await dbContext.TenantMembers.SingleAsync()).RemovedAt);
    }

    [Fact]
    public async Task DuplicateMessageIsIgnored()
    {
        await using var dbContext = CreateDbContext();
        var ingestor = new ClerkWebhookIngestor(dbContext);
        var payload = MembershipPayload("organizationMembership.created", "org:admin");

        await ingestor.Ingest("message-1", payload, CancellationToken.None);
        await ingestor.Ingest("message-1", payload, CancellationToken.None);

        Assert.Equal(1, await dbContext.Tenants.CountAsync());
        Assert.Equal(1, await dbContext.TenantMembers.CountAsync());
        Assert.Equal(1, await dbContext.ClerkWebhookEvents.CountAsync());
    }

    private static string MembershipPayload(string type, string role)
    {
        return $$"""
            {
              "type": "{{type}}",
              "data": {
                "id": "membership_1",
                "role": "{{role}}",
                "organization": { "id": "org_family-1", "name": "Test family" },
                "public_user_data": { "user_id": "user_1" }
              }
            }
            """;
    }

    private static Tenant CreateTenant()
    {
        return new Tenant
        {
            ClerkOrganizationId = "org_family-1",
            Name = "Test family",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
    }

    private static FinyteDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FinyteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new FinyteDbContext(options);
    }
}
