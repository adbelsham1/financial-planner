namespace FinancialPlanner.Infrastructure.Identity;

public sealed class TenantMembership
{
    public Guid TenantId { get; init; }
    public Guid UserId { get; init; }
    public required TenantRole Role { get; set; }
}

public sealed class LegalEntityGrant
{
    public Guid LegalEntityId { get; init; }
    public Guid UserId { get; init; }
    public required TenantRole Role { get; set; }
}

public enum TenantRole
{
    Owner = 1,
    Editor = 2,
    Reviewer = 3,
    ReadOnly = 4
}
