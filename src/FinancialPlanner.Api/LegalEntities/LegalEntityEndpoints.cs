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
        group.MapPost("/{legalEntityId:guid}/contributions/{contributionId:guid}/reverse", ReverseContributionAsync)
            .RequireAuthorization("TenantManage");
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
            var ownershipSnapshot = snapshot.ToEntity();
            database.OwnershipSnapshots.Add(ownershipSnapshot);
            audit.Add(context, "legal-entity.ownership-snapshot-created", $"ownership-snapshot:{ownershipSnapshot.Id}", "success",
                tenantId, actorId);
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
        if (entity.ArchivedAt is not null)
        {
            return Results.Conflict(new { detail = "Archived legal entities cannot receive new ownership records." });
        }
        if (!TryCreateSnapshot(entity.Id, request.EffectiveFrom, request.Parties, out var snapshot, out var errors))
        {
            return Results.ValidationProblem(errors);
        }
        var ownershipSnapshot = snapshot!.ToEntity(request.SupersedesOwnershipSnapshotId);
        if (request.SupersedesOwnershipSnapshotId is not null)
        {
            var superseded = await database.OwnershipSnapshots.SingleOrDefaultAsync(item =>
                item.Id == request.SupersedesOwnershipSnapshotId && item.LegalEntityId == entity.Id, cancellationToken);
            if (superseded is null || superseded.EffectiveFrom != request.EffectiveFrom)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["supersedesOwnershipSnapshotId"] = ["A revision must supersede an ownership snapshot with the same effective date."]
                });
            }
            if (await database.OwnershipSnapshots.AnyAsync(item =>
                item.SupersedesOwnershipSnapshotId == superseded.Id, cancellationToken))
            {
                return Results.Conflict(new { detail = "The selected ownership snapshot has already been superseded." });
            }
        }
        else
        {
            var latestEffectiveFrom = await database.OwnershipSnapshots
                .Where(item => item.LegalEntityId == entity.Id)
                .MaxAsync(item => (DateOnly?)item.EffectiveFrom, cancellationToken);
            if (latestEffectiveFrom is not null && request.EffectiveFrom <= latestEffectiveFrom)
            {
                return Results.Conflict(new { detail = "A new ownership snapshot must take effect after the current history." });
            }
        }
        database.OwnershipSnapshots.Add(ownershipSnapshot);
        audit.Add(context, "legal-entity.ownership-snapshot-created", $"ownership-snapshot:{ownershipSnapshot.Id}", "success",
            tenantId, actorId);
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
        if (entity.ArchivedAt is not null)
        {
            return Results.Conflict(new { detail = "Archived legal entities cannot receive new contribution records." });
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
        var contribution = new OwnershipContribution
        {
            LegalEntityId = entity.Id,
            PartyName = request.PartyName.Trim(),
            EffectiveOn = request.EffectiveOn,
            Amount = request.Amount,
            Currency = "AUD"
        };
        database.OwnershipContributions.Add(contribution);
        audit.Add(context, "legal-entity.contribution-recorded", $"ownership-contribution:{contribution.Id}", "success",
            tenantId, actorId);
        await database.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/legal-entities/{entity.Id}", null);
    }

    private static async Task<IResult> ReverseContributionAsync(Guid legalEntityId, Guid contributionId,
        ReverseOwnershipContributionRequest request, ClaimsPrincipal principal, PlannerDbContext database, AuditService audit,
        HttpContext context, CancellationToken cancellationToken)
    {
        if (!TryGetTenant(principal, out var tenantId, out var actorId)) return Results.Forbid();
        var entity = await FindForTenantAsync(legalEntityId, tenantId, database, cancellationToken);
        if (entity is null) return Results.NotFound();
        if (entity.ArchivedAt is not null)
            return Results.Conflict(new { detail = "Archived legal entities cannot receive new contribution records." });
        var original = await database.OwnershipContributions.SingleOrDefaultAsync(item =>
            item.Id == contributionId && item.LegalEntityId == entity.Id, cancellationToken);
        if (original is null || original.Amount <= 0)
            return Results.NotFound();
        if (request.EffectiveOn == DateOnly.MinValue)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["effectiveOn"] = ["An effective date is required."] });
        if (await database.OwnershipContributions.AnyAsync(item => item.ReversesOwnershipContributionId == original.Id,
                cancellationToken))
            return Results.Conflict(new { detail = "The contribution has already been reversed." });
        var reversal = new OwnershipContribution
        {
            LegalEntityId = entity.Id,
            ReversesOwnershipContributionId = original.Id,
            PartyName = original.PartyName,
            EffectiveOn = request.EffectiveOn,
            Amount = -original.Amount,
            Currency = "AUD"
        };
        database.OwnershipContributions.Add(reversal);
        audit.Add(context, "legal-entity.contribution-reversed", $"ownership-contribution:{reversal.Id}", "success",
            tenantId, actorId);
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
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 256) errors["name"] = ["A name of up to 256 characters is required."];
        return errors.Count == 0;
    }

    private static bool TryCreateSnapshot(Guid legalEntityId, DateOnly? effectiveFrom,
        IReadOnlyList<OwnershipPartyRequest?>? parties, out SnapshotDraft? snapshot, out Dictionary<string, string[]> errors)
    {
        errors = [];
        snapshot = null;
        if (effectiveFrom is null) errors["effectiveFrom"] = ["An effective date is required."];
        if (parties is null || parties.Count < 2) errors["parties"] = ["At least two ownership parties are required."];
        if (errors.Count > 0) return false;
        var rawParties = parties!;
        if (rawParties.Any(item => item is null))
        {
            errors["parties"] = ["Ownership parties cannot be null."];
            return false;
        }
        var suppliedParties = rawParties.Cast<OwnershipPartyRequest>().ToList();
        if (suppliedParties.Any(item => string.IsNullOrWhiteSpace(item.Name) || item.Name.Trim().Length > 128 ||
                item.Percentage <= 0 || item.Percentage > 100m || !HasAtMostScale(item.Percentage, 4)) ||
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
        if (request.Amount <= 0 || request.Amount > 9_999_999_999_999_999.99m || !HasAtMostScale(request.Amount, 2))
            errors["amount"] = ["A positive AUD contribution with no more than two decimal places is required."];
        return errors.Count == 0;
    }

    private static bool HasAtMostScale(decimal value, int scale) => value == decimal.Round(value, scale, MidpointRounding.ToZero);

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
            var supersededIds = entitySnapshots.Where(item => item.SupersedesOwnershipSnapshotId is not null)
                .Select(item => item.SupersedesOwnershipSnapshotId!.Value).ToHashSet();
            var current = entitySnapshots.Where(item => !supersededIds.Contains(item.Id) && item.EffectiveFrom <= today)
                .OrderByDescending(item => item.EffectiveFrom).FirstOrDefault();
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
        public OwnershipSnapshot ToEntity(Guid? supersedesOwnershipSnapshotId = null) => new()
        {
            LegalEntityId = LegalEntityId,
            SupersedesOwnershipSnapshotId = supersedesOwnershipSnapshotId,
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

    private sealed record SnapshotResponse(Guid Id, Guid? SupersedesOwnershipSnapshotId, DateOnly EffectiveFrom,
        IReadOnlyList<OwnershipPartyResponse> Parties)
    {
        public static SnapshotResponse From(OwnershipSnapshot snapshot) =>
            new(snapshot.Id, snapshot.SupersedesOwnershipSnapshotId, snapshot.EffectiveFrom,
                snapshot.Parties.Select(item => new OwnershipPartyResponse(item.Name, item.Percentage,
                    item.IsWorkspaceOwner)).ToList());
    }

    private sealed record OwnershipPartyResponse(string Name, decimal Percentage, bool IsWorkspaceOwner);

    private sealed record ContributionResponse(Guid Id, Guid? ReversesOwnershipContributionId, string PartyName,
        DateOnly EffectiveOn, decimal Amount, string Currency)
    {
        public static ContributionResponse From(OwnershipContribution contribution) =>
            new(contribution.Id, contribution.ReversesOwnershipContributionId, contribution.PartyName,
                contribution.EffectiveOn, contribution.Amount, contribution.Currency);
    }
}
