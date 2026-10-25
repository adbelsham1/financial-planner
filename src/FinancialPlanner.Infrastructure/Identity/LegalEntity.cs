namespace FinancialPlanner.Infrastructure.Identity;

public sealed class LegalEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public required string Name { get; set; }
    public required LegalEntityKind Kind { get; init; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

public enum LegalEntityKind
{
    Personal = 1,
    Smsf = 2,
    Joint = 3
}

public sealed class OwnershipSnapshot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid LegalEntityId { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public ICollection<OwnershipParty> Parties { get; init; } = new List<OwnershipParty>();
}

public sealed class OwnershipParty
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid OwnershipSnapshotId { get; init; }
    public required string Name { get; init; }
    public decimal Percentage { get; init; }
    public bool IsWorkspaceOwner { get; init; }
}

public sealed class OwnershipContribution
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid LegalEntityId { get; init; }
    public required string PartyName { get; init; }
    public DateOnly EffectiveOn { get; init; }
    public decimal Amount { get; init; }
    public required string Currency { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
