using System.Net.Mail;
using Finyte.Api.Tenancy;
using Finyte.Core.Scheduling;
using Finyte.Core.Tenancy;
using Finyte.Data;
using Finyte.Data.Analytics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Api.Endpoints;

public static class FamilyEndpoints
{
    public static IEndpointRouteBuilder MapFamilyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/family").RequireAuthorization();
        group.MapGet("/", GetFamily).WithName("GetFamily");
        group.MapPut("/settings", UpdateSettings).WithName("UpdateFamilySettings");
        group.MapPost("/invitations", Invite).WithName("InviteFamilyMember");
        group.MapDelete("/invitations/{invitationId:guid}", RevokeInvitation).WithName("RevokeFamilyInvitation");
        group.MapPost("/invitations/{invitationId:guid}/dev-accept", AcceptDevelopmentInvitation).WithName("AcceptDevelopmentFamilyInvitation");
        group.MapDelete("/members/{memberId:guid}", RemoveMember).WithName("RemoveFamilyMember");
        return app;
    }

    private static async Task<Ok<FamilyResponse>> GetFamily(
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IWebHostEnvironment environment,
        FinyteDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var tenant = await dbContext.Tenants.AsNoTracking().SingleAsync(x => x.Id == currentTenant.TenantId, cancellationToken);
        var members = await dbContext.TenantMembers
            .AsNoTracking()
            .Where(x => x.TenantId == currentTenant.TenantId && x.RemovedAt == null)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new FamilyMemberResponse(
                x.Id,
                x.UserId,
                x.DisplayName ?? (x.UserId == currentTenant.UserId ? "You" : "Family member"),
                x.Email,
                x.Role.ToString(),
                x.UserId == currentTenant.UserId))
            .ToListAsync(cancellationToken);
        var invitations = await dbContext.FamilyInvitations
            .AsNoTracking()
            .Where(x => x.TenantId == currentTenant.TenantId && x.Status == FamilyInvitationStatus.Pending)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new FamilyInvitationResponse(x.Id, x.Email, x.Role, x.Status, x.CreatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new FamilyResponse(
            tenant.Id,
            tenant.ClerkOrganizationId,
            tenant.Name,
            tenant.TimeZoneId,
            currentTenant.Role == TenantRole.Owner,
            environment.IsDevelopment(),
            members,
            invitations));
    }

    private static async Task<Results<Created<FamilyInvitationResponse>, BadRequest<string>, Conflict<string>, ForbidHttpResult>> Invite(
        InviteRequest request,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IWebHostEnvironment environment,
        IClerkOrganizationClient clerkOrganizationClient,
        FinyteDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (currentTenant.Role != TenantRole.Owner)
        {
            return TypedResults.Forbid();
        }

        if (!MailAddress.TryCreate(request.Email?.Trim(), out var email))
        {
            return TypedResults.BadRequest("A valid email address is required.");
        }

        var normalizedEmail = email.Address.ToLowerInvariant();
        var exists = await dbContext.FamilyInvitations.AnyAsync(x => x.TenantId == currentTenant.TenantId
            && x.Email == normalizedEmail
            && x.Status == FamilyInvitationStatus.Pending,
            cancellationToken);
        if (exists)
        {
            return TypedResults.Conflict("That email already has a pending invitation.");
        }

        var tenant = await dbContext.Tenants.SingleAsync(x => x.Id == currentTenant.TenantId, cancellationToken);
        var invitationId = environment.IsDevelopment()
            ? $"dev_invitation_{Guid.NewGuid():N}"
            : await clerkOrganizationClient.CreateInvitation(
                tenant.ClerkOrganizationId,
                currentTenant.UserId,
                normalizedEmail,
                cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var invitation = new FamilyInvitation
        {
            TenantId = tenant.Id,
            ProviderInvitationId = invitationId,
            Email = normalizedEmail,
            Role = "org:member",
            Status = FamilyInvitationStatus.Pending,
            InvitedByUserId = currentTenant.UserId,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.FamilyInvitations.Add(invitation);
        await dbContext.SaveChangesAsync(cancellationToken);
        var response = ToResponse(invitation);
        return TypedResults.Created($"/api/family/invitations/{invitation.Id}", response);
    }

    private static async Task<IResult> RevokeInvitation(
        Guid invitationId,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IWebHostEnvironment environment,
        IClerkOrganizationClient clerkOrganizationClient,
        FinyteDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (currentTenant.Role != TenantRole.Owner)
        {
            return TypedResults.Forbid();
        }

        var invitation = await dbContext.FamilyInvitations.SingleOrDefaultAsync(x => x.Id == invitationId
            && x.TenantId == currentTenant.TenantId
            && x.Status == FamilyInvitationStatus.Pending,
            cancellationToken);
        if (invitation is null)
        {
            return TypedResults.NotFound();
        }

        if (!environment.IsDevelopment())
        {
            var organizationId = await dbContext.Tenants
                .Where(x => x.Id == currentTenant.TenantId)
                .Select(x => x.ClerkOrganizationId)
                .SingleAsync(cancellationToken);
            await clerkOrganizationClient.RevokeInvitation(organizationId, invitation.ProviderInvitationId, currentTenant.UserId, cancellationToken);
        }

        invitation.Status = FamilyInvitationStatus.Revoked;
        invitation.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> AcceptDevelopmentInvitation(
        Guid invitationId,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IWebHostEnvironment environment,
        FinyteDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return TypedResults.NotFound();
        }

        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (currentTenant.Role != TenantRole.Owner)
        {
            return TypedResults.Forbid();
        }

        var invitation = await dbContext.FamilyInvitations.SingleOrDefaultAsync(x => x.Id == invitationId
            && x.TenantId == currentTenant.TenantId
            && x.Status == FamilyInvitationStatus.Pending,
            cancellationToken);
        if (invitation is null)
        {
            return TypedResults.NotFound();
        }

        var member = new TenantMember
        {
            TenantId = currentTenant.TenantId,
            UserId = $"dev-member-{invitation.Id:N}",
            ClerkMembershipId = $"dev_membership_{invitation.Id:N}",
            DisplayName = invitation.Email.Split('@')[0],
            Email = invitation.Email,
            Role = TenantRole.Member,
            CreatedAt = DateTimeOffset.UtcNow
        };
        invitation.Status = FamilyInvitationStatus.Accepted;
        invitation.UpdatedAt = DateTimeOffset.UtcNow;
        dbContext.TenantMembers.Add(member);
        await dbContext.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new FamilyMemberResponse(member.Id, member.UserId, member.DisplayName, member.Email, member.Role.ToString(), IsCurrent: false));
    }

    private static async Task<IResult> RemoveMember(
        Guid memberId,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IWebHostEnvironment environment,
        IClerkOrganizationClient clerkOrganizationClient,
        FinyteDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (currentTenant.Role != TenantRole.Owner)
        {
            return TypedResults.Forbid();
        }

        var member = await dbContext.TenantMembers.SingleOrDefaultAsync(x => x.Id == memberId
            && x.TenantId == currentTenant.TenantId
            && x.UserId != currentTenant.UserId
            && x.RemovedAt == null,
            cancellationToken);
        if (member is null)
        {
            return TypedResults.NotFound();
        }

        if (!environment.IsDevelopment())
        {
            var organizationId = await dbContext.Tenants
                .Where(x => x.Id == currentTenant.TenantId)
                .Select(x => x.ClerkOrganizationId)
                .SingleAsync(cancellationToken);
            await clerkOrganizationClient.RemoveMember(organizationId, member.UserId, cancellationToken);
        }

        member.RemovedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<FamilySettingsResponse>, BadRequest<string>, ForbidHttpResult>> UpdateSettings(
        FamilySettingsRequest request,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        FinyteDbContext dbContext,
        IProjectionInvalidator projectionInvalidator,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (currentTenant.Role != TenantRole.Owner)
        {
            return TypedResults.Forbid();
        }

        var timeZoneId = request.TimeZoneId?.Trim();
        if (timeZoneId is null || timeZoneId.Length > 64 || !FinancialCalendar.IsValidTimeZoneId(timeZoneId))
        {
            return TypedResults.BadRequest("Choose a valid time zone, such as Australia/Sydney.");
        }

        var tenant = await dbContext.Tenants.SingleAsync(x => x.Id == currentTenant.TenantId, cancellationToken);
        if (tenant.TimeZoneId != timeZoneId)
        {
            tenant.TimeZoneId = timeZoneId;
            // Every day and month boundary moves with the zone, so cached overviews must rebuild.
            await projectionInvalidator.TenantProjectionDataChanged(tenant.Id, "household time zone changed", cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(new FamilySettingsResponse(tenant.TimeZoneId));
    }

    private static FamilyInvitationResponse ToResponse(FamilyInvitation invitation)
    {
        return new FamilyInvitationResponse(invitation.Id, invitation.Email, invitation.Role, invitation.Status, invitation.CreatedAt);
    }

    private sealed record InviteRequest(string? Email);

    private sealed record FamilySettingsRequest(string? TimeZoneId);

    private sealed record FamilySettingsResponse(string TimeZoneId);

    private sealed record FamilyResponse(
        Guid Id,
        string OrganizationId,
        string Name,
        string TimeZoneId,
        bool CanManage,
        bool IsDevelopment,
        IReadOnlyList<FamilyMemberResponse> Members,
        IReadOnlyList<FamilyInvitationResponse> Invitations);

    private sealed record FamilyMemberResponse(Guid Id, string UserId, string? DisplayName, string? Email, string Role, bool IsCurrent);

    private sealed record FamilyInvitationResponse(Guid Id, string Email, string Role, string Status, DateTimeOffset CreatedAt);
}
