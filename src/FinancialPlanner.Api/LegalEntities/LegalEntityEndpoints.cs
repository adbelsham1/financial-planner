using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FinancialPlanner.Api.Auth;
using FinancialPlanner.Infrastructure;
using FinancialPlanner.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace FinancialPlanner.Api.LegalEntities;

public static class LegalEntityEndpoints
{
    public static IEndpointRouteBuilder MapLegalEntityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/legal-entities").RequireAuthorization("TenantManage");
        group.MapGet("/", ListAsync);
        group.MapGet("/{legalEntityId:guid}", GetAsync);
        group.MapPost("/", CreateAsync).RequireAuthorization("TenantManage");
        group.MapPut("/{legalEntityId:guid}", RenameAsync).RequireAuthorization("TenantManage");
        group.MapPost("/{legalEntityId:guid}/archive", ArchiveAsync).RequireAuthorization("TenantManage");
        group.MapPost("/{legalEntityId:guid}/ownership-snapshots", CreateOwnershipSnapshotAsync)
            .RequireAuthorization("TenantManage");
        group.MapPost("/{legalEntityId:guid}/contributions", CreateContributionAsync).RequireAuthorization("TenantManage");
        return endpoints;
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal principal, PlannerDbContext database,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenant(principal, out var tenantId, out _)) return Results.Forbid();
        var entities = await database.LegalEntities.AsNoTracking()
            .Where(entity => entity.TenantId == tenantId)
            .OrderBy(entity => entity.ArchivedAt != null).ThenBy(entity => entity.Name)
            .ToListAsync(cancellationToken);
        return Results.Ok(await ToResponsesAsync(entities, database, cancellationToken));
    }

    private static async Task<IResult> GetAsync(Guid legalEntityId, ClaimsPrincipal principal, PlannerDbContext database,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenant(principal, out var tenantId, out _)) return Results.Forbid();
        var entity = await FindForTenantAsync(legalEntityId, tenantId, database, cancellationToken);
        if (entity is null) return Results.NotFound();
        return Results.Ok((await ToResponsesAsync([entity], database, cancellationToken)).Single());
    }

    private static async Task<IResult> CreateAsync(CreateLegalEntityRequest request, ClaimsPrincipal principal,
        PlannerDbContext database, AuditService audit, HttpContext context, CancellationToken cancellationToken)
    {
        if (!TryGetTenant(principal, out var tenantId, out var actorId)) return Results.Forbid();
        if (!TryValidateName(request.Name, out var errors) ||
            !Enum.TryParse<LegalEntityKind>(request.Kind, true, out var kind) ||
            !Enum.IsDefined(kind))
        {
            errors["kind"] = ["A personal, SMSF, or joint entity type is required."];
            return Results.ValidationProblem(errors);
        }

        SnapshotDraft? snapshot = null;
        if (kind == LegalEntityKind.Joint &&
            !TryCreateSnapshot(Guid.Empty, request.EffectiveFrom, request.OwnershipParties, out snapshot, out errors))
        {
            return Results.ValidationProblem(errors);
        }
        if (kind != LegalEntityKind.Joint && request.OwnershipParties is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ownershipParties"] = ["Only joint entities may have ownership parties."]
            });
        }

        var entity = new LegalEntity { TenantId = tenantId, Name = request.Name.Trim(), Kind = kind };
        if (snapshot is not null)
        {
            snapshot = snapshot with { LegalEntityId = entity.Id };
            database.OwnershipSnapshots.Add(snapshot.ToEntity());
        }
        database.Add(entity);
        database.LegalEntityGrants.Add(new LegalEntityGrant
        {
            LegalEntityId = entity.Id,
            UserId = actorId,
            Role = TenantRole.Owner
        });
        audit.Add(context, "legal-entity.created", $"legal-entity:{entity.Id}", "success", tenantId, actorId);
        await database.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/legal-entities/{entity.Id}", (await ToResponsesAsync([entity], database, cancellationToken)).Single());
    }

    private static async Task<IResult> RenameAsync(Guid legalEntityId, RenameLegalEntityRequest request,
        ClaimsPrincipal principal, PlannerDbContext database, AuditService audit, HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenant(principal, out var tenantId, out var actorId)) return Results.Forbid();
        if (!TryValidateName(request.Name, out var errors)) return Results.ValidationProblem(errors);
        var entity = await FindForTenantAsync(legalEntityId, tenantId, database, cancellationToken);
        if (entity is null) return Results.NotFound();
        entity.Name = request.Name.Trim();
        audit.Add(context, "legal-entity.renamed", $"legal-entity:{entity.Id}", "success", tenantId, actorId);
        await database.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ArchiveAsync(Guid legalEntityId, ClaimsPrincipal principal, PlannerDbContext database,
        AuditService audit, HttpContext context, CancellationToken cancellationToken)
    {
        if (!TryGetTenant(principal, out var tenantId, out var actorId)) return Results.Forbid();
        var entity = await FindForTenantAsync(legalEntityId, tenantId, database, cancellationToken);
        if (entity is null) return Results.NotFound();
        if (entity.ArchivedAt is null)
        {
            entity.ArchivedAt = DateTimeOffset.UtcNow;
            audit.Add(context, "legal-entity.archived", $"legal-entity:{entity.Id}", "success", tenantId, actorId);
            await database.SaveChangesAsync(cancellationToken);
        }
        return Results.NoContent();
    }

    private static async Task<IResult> CreateOwnershipSnapshotAsync(Guid legalEntityId,
        CreateOwnershipSnapshotRequest request, ClaimsPrincipal principal, PlannerDbContext database, AuditService audit,
        HttpContext context, CancellationToken cancellationToken)
    {
        if (!TryGetTenant(principal, out var tenantId, out var actorId)) return Results.Forbid();
        var entity = await FindForTenantAsync(legalEntityId, tenantId, database, cancellationToken);
        if (entity is null) return Results.NotFound();
        if (entity.Kind != LegalEntityKind.Joint)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["legalEntityId"] = ["Ownership is only recorded for joint entities."] });
        }
        if (!TryCreateSnapshot(entity.Id, request.EffectiveFrom, request.Parties, out var snapshot, out var errors))
        {
            return Results.ValidationProblem(errors);
        }
        var latestEffectiveFrom = await database.OwnershipSnapshots
            .Where(item => item.LegalEntityId == entity.Id)
            .MaxAsync(item => (DateOnly?)item.EffectiveFrom, cancellationToken);
        if (latestEffectiveFrom is not null && request.EffectiveFrom <= latestEffectiveFrom)
        {
            return Results.Conflict(new { detail = "A replacement ownership snapshot must take effect after the current history." });
        }
        database.OwnershipSnapshots.Add(snapshot!.ToEntity());
        audit.Add(context, "legal-entity.ownership-snapshot-created", $"legal-entity:{entity.Id}", "success", tenantId, actorId);
        await database.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/legal-entities/{entity.Id}", null);
    }

    private static async Task<IResult> CreateContributionAsync(Guid legalEntityId, CreateOwnershipContributionRequest request,
        ClaimsPrincipal principal, PlannerDbContext database, AuditService audit, HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenant(principal, out var tenantId, out var actorId)) return Results.Forbid();
        var entity = await FindForTenantAsync(legalEntityId, tenantId, database, cancellationToken);
        if (entity is null) return Results.NotFound();
        if (entity.Kind != LegalEntityKind.Joint)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["legalEntityId"] = ["Contributions are only recorded for joint entities."] });
        }
        if (!TryValidateContribution(request, out var errors)) return Results.ValidationProblem(errors);
        var snapshot = await database.OwnershipSnapshots.Include(item => item.Parties)
            .Where(item => item.LegalEntityId == entity.Id && item.EffectiveFrom <= request.EffectiveOn)
            .OrderByDescending(item => item.EffectiveFrom).FirstOrDefaultAsync(cancellationToken);
        if (snapshot is null || !snapshot.Parties.Any(item => string.Equals(item.Name, request.PartyName.Trim(),
                StringComparison.OrdinalIgnoreCase)))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["partyName"] = ["The contributor must be a party in the ownership snapshot effective on that date."]
            });
        }
        database.OwnershipContributions.Add(new OwnershipContribution
        {
            LegalEntityId = entity.Id,
            PartyName = request.PartyName.Trim(),
            EffectiveOn = request.EffectiveOn,
            Amount = request.Amount,
            Currency = "AUD"
        });
        audit.Add(context, "legal-entity.contribution-recorded", $"legal-entity:{entity.Id}", "success", tenantId, actorId);
        await database.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/legal-entities/{entity.Id}", null);
    }

    private static bool TryGetTenant(ClaimsPrincipal principal, out Guid tenantId, out Guid actorId)
    {
        tenantId = Guid.Empty;
        actorId = Guid.Empty;
        return Guid.TryParse(principal.FindFirstValue("tenant_id"), out tenantId) &&
            Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
                principal.FindFirstValue(ClaimTypes.NameIdentifier), out actorId);
    }

    private static Task<LegalEntity?> FindForTenantAsync(Guid legalEntityId, Guid tenantId, PlannerDbContext database,
        CancellationToken cancellationToken) =>
        database.LegalEntities.SingleOrDefaultAsync(item => item.Id == legalEntityId && item.TenantId == tenantId, cancellationToken);

    private static bool TryValidateName(string? name, out Dictionary<string, string[]> errors)
    {
        errors = [];
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 128) errors["name"] = ["A name of up to 128 characters is required."];
        return errors.Count == 0;
    }

    private static bool TryCreateSnapshot(Guid legalEntityId, DateOnly? effectiveFrom,
        IReadOnlyList<OwnershipPartyRequest>? parties, out SnapshotDraft? snapshot, out Dictionary<string, string[]> errors)
    {
        errors = [];
        snapshot = null;
        if (effectiveFrom is null) errors["effectiveFrom"] = ["An effective date is required."];
        if (parties is null || parties.Count < 2) errors["parties"] = ["At least two ownership parties are required."];
        if (errors.Count > 0) return false;
        var suppliedParties = parties!;
        if (suppliedParties.Any(item => string.IsNullOrWhiteSpace(item.Name) || item.Name.Trim().Length > 128 || item.Percentage <= 0) ||
            suppliedParties.Count(item => item.IsWorkspaceOwner) != 1 ||
            suppliedParties.Sum(item => item.Percentage) != 100m ||
            suppliedParties.Select(item => item.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != suppliedParties.Count)
        {
            errors["parties"] = ["Parties must have unique names, positive percentages totalling exactly 100.0000%, and exactly one workspace owner."];
            return false;
        }
        snapshot = new SnapshotDraft(legalEntityId, effectiveFrom!.Value, suppliedParties);
        return true;
    }

    private static bool TryValidateContribution(CreateOwnershipContributionRequest request,
        out Dictionary<string, string[]> errors)
    {
        errors = [];
        if (string.IsNullOrWhiteSpace(request.PartyName) || request.PartyName.Trim().Length > 128)
            errors["partyName"] = ["A party name of up to 128 characters is required."];
        if (request.Amount <= 0) errors["amount"] = ["A positive AUD contribution amount is required."];
        return errors.Count == 0;
    }

    private static async Task<IReadOnlyList<LegalEntityResponse>> ToResponsesAsync(IReadOnlyList<LegalEntity> entities,
        PlannerDbContext database, CancellationToken cancellationToken)
    {
        var entityIds = entities.Select(item => item.Id).ToArray();
        var snapshots = await database.OwnershipSnapshots.AsNoTracking().Include(item => item.Parties)
            .Where(item => entityIds.Contains(item.LegalEntityId)).OrderByDescending(item => item.EffectiveFrom)
            .ToListAsync(cancellationToken);
        var contributions = await database.OwnershipContributions.AsNoTracking()
            .Where(item => entityIds.Contains(item.LegalEntityId)).OrderByDescending(item => item.EffectiveOn)
            .ToListAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return entities.Select(entity =>
        {
            var entitySnapshots = snapshots.Where(item => item.LegalEntityId == entity.Id).ToList();
            var current = entitySnapshots.FirstOrDefault(item => item.EffectiveFrom <= today);
            var ownerShare = entity.Kind == LegalEntityKind.Joint
                ? current?.Parties.Single(item => item.IsWorkspaceOwner).Percentage ?? 0m
                : 100m;
            return new LegalEntityResponse(entity.Id, entity.Name, entity.Kind.ToString(), entity.ArchivedAt, 100m,
                ownerShare, entitySnapshots.Select(SnapshotResponse.From).ToList(),
                contributions.Where(item => item.LegalEntityId == entity.Id).Select(ContributionResponse.From).ToList());
        }).ToList();
    }

    private sealed record SnapshotDraft(Guid LegalEntityId, DateOnly EffectiveFrom, IReadOnlyList<OwnershipPartyRequest> Parties)
    {
        public OwnershipSnapshot ToEntity() => new()
        {
            LegalEntityId = LegalEntityId,
            EffectiveFrom = EffectiveFrom,
            Parties = Parties.Select(party => new OwnershipParty
            {
                Name = party.Name.Trim(),
                Percentage = party.Percentage,
                IsWorkspaceOwner = party.IsWorkspaceOwner
            }).ToList()
        };
    }

    private sealed record LegalEntityResponse(Guid Id, string Name, string Kind, DateTimeOffset? ArchivedAt,
        decimal LegalOwnershipPercentage, decimal OwnerAttributablePercentage, IReadOnlyList<SnapshotResponse> OwnershipHistory,
        IReadOnlyList<ContributionResponse> Contributions);

    private sealed record SnapshotResponse(DateOnly EffectiveFrom, IReadOnlyList<OwnershipPartyResponse> Parties)
    {
        public static SnapshotResponse From(OwnershipSnapshot snapshot) =>
            new(snapshot.EffectiveFrom, snapshot.Parties.Select(item => new OwnershipPartyResponse(item.Name, item.Percentage,
                item.IsWorkspaceOwner)).ToList());
    }

    private sealed record OwnershipPartyResponse(string Name, decimal Percentage, bool IsWorkspaceOwner);

    private sealed record ContributionResponse(string PartyName, DateOnly EffectiveOn, decimal Amount, string Currency)
    {
        public static ContributionResponse From(OwnershipContribution contribution) =>
            new(contribution.PartyName, contribution.EffectiveOn, contribution.Amount, contribution.Currency);
    }
}
