using FinancialPlanner.Infrastructure.Identity;
using FinancialPlanner.Infrastructure.Ledger;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FinancialPlanner.Infrastructure;

public sealed class PlannerDbContext(DbContextOptions<PlannerDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();
    public DbSet<OwnershipSnapshot> OwnershipSnapshots => Set<OwnershipSnapshot>();
    public DbSet<OwnershipParty> OwnershipParties => Set<OwnershipParty>();
    public DbSet<OwnershipContribution> OwnershipContributions => Set<OwnershipContribution>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<LegalEntityGrant> LegalEntityGrants => Set<LegalEntityGrant>();
    public DbSet<EmailOtp> EmailOtps => Set<EmailOtp>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<LedgerJournal> LedgerJournals => Set<LedgerJournal>();
    public DbSet<LedgerJournalLine> LedgerJournalLines => Set<LedgerJournalLine>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<TenantMembership>().HasKey(item => new { item.TenantId, item.UserId });
        builder.Entity<LegalEntityGrant>().HasKey(item => new { item.LegalEntityId, item.UserId });
        builder.Entity<LegalEntity>().HasIndex(item => new { item.TenantId, item.Name });
        builder.Entity<LegalEntity>().Property(item => item.Name).HasMaxLength(256);
        builder.Entity<OwnershipSnapshot>().HasIndex(item => new { item.LegalEntityId, item.EffectiveFrom });
        builder.Entity<OwnershipSnapshot>().HasOne<LegalEntity>().WithMany()
            .HasForeignKey(item => item.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<OwnershipSnapshot>().HasOne<OwnershipSnapshot>().WithMany()
            .HasForeignKey(item => item.SupersedesOwnershipSnapshotId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<OwnershipSnapshot>().HasMany(item => item.Parties).WithOne()
            .HasForeignKey(item => item.OwnershipSnapshotId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<OwnershipParty>().Property(item => item.Name).HasMaxLength(128);
        builder.Entity<OwnershipParty>().Property(item => item.Percentage).HasPrecision(9, 4);
        builder.Entity<OwnershipContribution>().HasIndex(item => new { item.LegalEntityId, item.EffectiveOn });
        builder.Entity<OwnershipContribution>().HasOne<LegalEntity>().WithMany()
            .HasForeignKey(item => item.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<OwnershipContribution>().HasOne<OwnershipContribution>().WithMany()
            .HasForeignKey(item => item.ReversesOwnershipContributionId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<OwnershipContribution>().Property(item => item.PartyName).HasMaxLength(128);
        builder.Entity<OwnershipContribution>().Property(item => item.Amount).HasPrecision(18, 2);
        builder.Entity<OwnershipContribution>().Property(item => item.Currency).HasMaxLength(3);
        builder.Entity<EmailOtp>().HasIndex(item => new { item.UserId, item.Purpose, item.ExpiresAt });
        builder.Entity<EmailOtp>().HasIndex(item => new { item.SourceHash, item.Purpose, item.ExpiresAt });
        builder.Entity<EmailOtp>().Property(item => item.CodeHash).HasMaxLength(64);
        builder.Entity<RefreshToken>().HasIndex(item => item.TokenHash).IsUnique();
        builder.Entity<RefreshToken>().HasIndex(item => new { item.UserId, item.SessionId });
        builder.Entity<AuditEvent>().HasIndex(item => new { item.TenantId, item.OccurredAt });
        builder.Entity<AuditEvent>().Property(item => item.Action).HasMaxLength(128);
        builder.Entity<AuditEvent>().Property(item => item.Target).HasMaxLength(256);
        builder.Entity<AuditEvent>().Property(item => item.CorrelationId).HasMaxLength(128);
        builder.Entity<LedgerAccount>().HasIndex(item => new { item.LegalEntityId, item.Code }).IsUnique();
        builder.Entity<LedgerAccount>().HasIndex(item => new { item.TenantId, item.LegalEntityId });
        builder.Entity<LedgerAccount>().Property(item => item.Code).HasMaxLength(32);
        builder.Entity<LedgerAccount>().Property(item => item.Name).HasMaxLength(256);
        builder.Entity<LedgerAccount>().HasOne<LegalEntity>().WithMany()
            .HasForeignKey(item => item.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<LedgerJournal>().HasIndex(item => new { item.LegalEntityId, item.EffectiveOn, item.Status });
        builder.Entity<LedgerJournal>().Property(item => item.Description).HasMaxLength(512);
        builder.Entity<LedgerJournal>().HasOne<LegalEntity>().WithMany()
            .HasForeignKey(item => item.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<LedgerJournal>().HasOne<LedgerJournal>().WithMany()
            .HasForeignKey(item => item.ReversesJournalId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<LedgerJournal>().HasOne<LedgerJournal>().WithMany()
            .HasForeignKey(item => item.AdjustsJournalId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<LedgerJournal>().HasMany(item => item.Lines).WithOne()
            .HasForeignKey(item => item.LedgerJournalId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<LedgerJournalLine>().HasOne<LedgerAccount>().WithMany()
            .HasForeignKey(item => item.LedgerAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<LedgerJournalLine>().Property(item => item.Debit).HasPrecision(18, 2);
        builder.Entity<LedgerJournalLine>().Property(item => item.Credit).HasPrecision(18, 2);
        builder.Entity<LedgerJournalLine>().Property(item => item.GstRate).HasPrecision(5, 4);
    }

    public override int SaveChanges()
    {
        EnsureImmutableRecordsAreUnchanged();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnsureImmutableRecordsAreUnchanged();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void EnsureImmutableRecordsAreUnchanged()
    {
        if (ChangeTracker.Entries<AuditEvent>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<OwnershipSnapshot>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<OwnershipParty>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<OwnershipContribution>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<LedgerJournalLine>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<LedgerJournal>().Any(entry => entry.State is EntityState.Deleted ||
                entry.State is EntityState.Modified && entry.OriginalValues.GetValue<LedgerJournalStatus>(
                    nameof(LedgerJournal.Status)) == LedgerJournalStatus.Approved))
        {
            throw new InvalidOperationException("Audit events, ownership records, approved journals, and journal lines are append-only.");
        }
    }
}
