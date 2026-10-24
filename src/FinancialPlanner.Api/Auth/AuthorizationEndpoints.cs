using Microsoft.AspNetCore.Authorization;

namespace FinancialPlanner.Api.Auth;

public static class AuthorizationEndpoints
{
    public static IEndpointRouteBuilder MapAuthorizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/legal-entities").RequireAuthorization("TenantRead");
        group.MapGet("/{legalEntityId:guid}/authorization", CheckReadAccessAsync);
        group.MapPost("/{legalEntityId:guid}/review", CheckReviewAccessAsync);
        group.MapPut("/{legalEntityId:guid}/workspace-data", CheckWriteAccessAsync);
        group.MapDelete("/{legalEntityId:guid}/workspace-data", CheckManageAccessAsync);
        return endpoints;
    }

    private static Task<IResult> CheckReadAccessAsync(Guid legalEntityId, HttpContext context, LegalEntityAccessService access,
        CancellationToken cancellationToken) => CheckAsync(legalEntityId, TenantCapability.Read, context, access, cancellationToken);
    private static Task<IResult> CheckReviewAccessAsync(Guid legalEntityId, HttpContext context, LegalEntityAccessService access,
        CancellationToken cancellationToken) => CheckAsync(legalEntityId, TenantCapability.Review, context, access, cancellationToken);
    private static Task<IResult> CheckWriteAccessAsync(Guid legalEntityId, HttpContext context, LegalEntityAccessService access,
        CancellationToken cancellationToken) => CheckAsync(legalEntityId, TenantCapability.Write, context, access, cancellationToken);
    private static Task<IResult> CheckManageAccessAsync(Guid legalEntityId, HttpContext context, LegalEntityAccessService access,
        CancellationToken cancellationToken) => CheckAsync(legalEntityId, TenantCapability.Manage, context, access, cancellationToken);

    private static async Task<IResult> CheckAsync(Guid legalEntityId, TenantCapability capability, HttpContext context,
        LegalEntityAccessService access, CancellationToken cancellationToken) =>
        await access.CanAccessAsync(context.User, legalEntityId, capability, cancellationToken) ? Results.NoContent() : Results.Forbid();
}
