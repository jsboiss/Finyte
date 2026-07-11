using System.Net;
using System.Net.Http.Json;
using Finyte.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class FamilyOnboardingApiTests
{
    [Fact]
    public async Task CurrentUserDoesNotCreateFamily()
    {
        await using var factory = new FinyteApiFactory();
        var client = factory.CreateClient();

        var currentUser = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me");

        Assert.NotNull(currentUser);
        Assert.False(currentUser.Onboarding.HasFamily);
        Assert.Null(currentUser.TenantId);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        Assert.Empty(await dbContext.Tenants.ToListAsync());
        Assert.Empty(await dbContext.TenantMembers.ToListAsync());
    }

    [Fact]
    public async Task ProvisionFamilyCreatesMappedTenantAndOwnerMembership()
    {
        await using var factory = new FinyteApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/family", new { name = "The Test Family" });
        var currentUser = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(currentUser);
        Assert.True(currentUser.Onboarding.HasFamily);
        Assert.NotNull(currentUser.TenantId);
        Assert.Equal("Owner", currentUser.Role);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var tenant = await dbContext.Tenants.SingleAsync();
        var member = await dbContext.TenantMembers.SingleAsync();
        Assert.Equal("org_dev-family", tenant.ClerkOrganizationId);
        Assert.Equal("The Test Family", tenant.Name);
        Assert.Equal(tenant.Id, member.TenantId);
        Assert.Equal("dev-user", member.UserId);
    }

    [Fact]
    public async Task ProvisionFamilyIsIdempotent()
    {
        await using var factory = new FinyteApiFactory();
        var client = factory.CreateClient();

        var firstResponse = await client.PostAsJsonAsync("/api/auth/family", new { name = "Original family" });
        var firstUser = await firstResponse.Content.ReadFromJsonAsync<CurrentUserResponse>();
        var secondResponse = await client.PostAsJsonAsync("/api/auth/family", new { name = "Replacement family" });
        var secondUser = await secondResponse.Content.ReadFromJsonAsync<CurrentUserResponse>();

        Assert.NotNull(firstUser);
        Assert.NotNull(secondUser);
        Assert.Equal(firstUser.TenantId, secondUser.TenantId);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        Assert.Equal(1, await dbContext.Tenants.CountAsync());
        Assert.Equal(1, await dbContext.TenantMembers.CountAsync());
        Assert.Equal("Original family", (await dbContext.Tenants.SingleAsync()).Name);
    }

    private sealed record CurrentUserResponse(string? UserId, Guid? TenantId, string? Role, OnboardingState Onboarding);

    private sealed record OnboardingState(bool HasFamily);
}
