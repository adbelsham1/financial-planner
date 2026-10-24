using FinancialPlanner.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FinancialPlanner.Infrastructure;

public sealed class PlannerDbContext(DbContextOptions<PlannerDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<LegalEntityGrant> LegalEntityGrants => Set<LegalEntityGrant>();
    public DbSet<EmailOtp> EmailOtps => Set<EmailOtp>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<TenantMembership>().HasKey(item => new { item.TenantId, item.UserId });
        builder.Entity<LegalEntityGrant>().HasKey(item => new { item.LegalEntityId, item.UserId });
        builder.Entity<EmailOtp>().HasIndex(item => new { item.UserId, item.Purpose, item.ExpiresAt });
        builder.Entity<RefreshToken>().HasIndex(item => item.TokenHash).IsUnique();
        builder.Entity<AuditEvent>().HasIndex(item => new { item.TenantId, item.OccurredAt });
    }
}
