namespace FinancialPlanner.Infrastructure.Identity;

public sealed class EmailOtp
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid UserId { get; init; }
    public required string Purpose { get; init; }
    public required string CodeHash { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}

public sealed class RefreshToken
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid UserId { get; init; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedById { get; set; }
    public required Guid SessionId { get; init; }
}

public sealed class AuditEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? TenantId { get; init; }
    public Guid? ActorId { get; init; }
    public required string Action { get; init; }
    public required string Target { get; init; }
    public required string Outcome { get; init; }
    public required string CorrelationId { get; init; }
    public string? HashedIpAddress { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
