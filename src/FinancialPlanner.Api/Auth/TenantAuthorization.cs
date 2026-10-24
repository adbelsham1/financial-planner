using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FinancialPlanner.Infrastructure;
using FinancialPlanner.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace FinancialPlanner.Api.Auth;

public enum TenantCapability { Read, Review, Write, Manage }

public sealed class TenantCapabilityRequirement(TenantCapability capability) : IAuthorizationRequirement
{
    public TenantCapability Capability { get; } = capability;
}

public sealed class TenantCapabilityHandler(PlannerDbContext database)
    : AuthorizationHandler<TenantCapabilityRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        TenantCapabilityRequirement requirement)
    {
        var sub = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tenant = context.User.FindFirstValue("tenant_id");
        if (!Guid.TryParse(sub, out var userId) || !Guid.TryParse(tenant, out var tenantId)) return;
        var role = await database.TenantMemberships.Where(x => x.UserId == userId && x.TenantId == tenantId)
            .Select(x => (TenantRole?)x.Role).SingleOrDefaultAsync();
        if (role is not null && Allows(role.Value, requirement.Capability)) context.Succeed(requirement);
    }

    public static bool Allows(TenantRole role, TenantCapability capability) => capability switch
    {
        TenantCapability.Read => true,
        TenantCapability.Review => role is TenantRole.Owner or TenantRole.Editor or TenantRole.Reviewer,
        TenantCapability.Write => role is TenantRole.Owner or TenantRole.Editor,
        TenantCapability.Manage => role is TenantRole.Owner,
        _ => false
    };
}

public sealed class LegalEntityAccessService(PlannerDbContext database)
{
    public async Task<bool> CanAccessAsync(ClaimsPrincipal principal, Guid legalEntityId, TenantCapability capability,
        CancellationToken cancellationToken)
    {
        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var tenant = principal.FindFirstValue("tenant_id");
        if (!Guid.TryParse(sub, out var userId) || !Guid.TryParse(tenant, out var tenantId)) return false;
        var entity = await database.LegalEntities.SingleOrDefaultAsync(x => x.Id == legalEntityId, cancellationToken);
        if (entity is null || entity.TenantId != tenantId) return false;
        var role = await database.TenantMemberships.Where(x => x.UserId == userId && x.TenantId == tenantId)
            .Select(x => (TenantRole?)x.Role).SingleOrDefaultAsync(cancellationToken);
        if (role is null || !TenantCapabilityHandler.Allows(role.Value, capability)) return false;
        return await database.LegalEntityGrants.AnyAsync(x => x.UserId == userId && x.LegalEntityId == legalEntityId &&
            TenantCapabilityHandler.Allows(x.Role, capability), cancellationToken);
    }
}
