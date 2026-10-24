namespace FinancialPlanner.Infrastructure.Identity;

public sealed class LegalEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public required string Name { get; set; }
    public required LegalEntityKind Kind { get; init; }
}

public enum LegalEntityKind
{
    Personal = 1,
    Smsf = 2,
    Joint = 3
}
