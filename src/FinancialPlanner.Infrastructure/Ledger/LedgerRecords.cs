namespace FinancialPlanner.Infrastructure.Ledger;

public enum LedgerAccountType
{
    Asset = 1,
    Liability = 2,
    Equity = 3,
    Income = 4,
    Expense = 5
}

public enum LedgerJournalType
{
    Manual = 1,
    OpeningBalance = 2
}

public enum LedgerJournalStatus
{
    Draft = 1,
    Approved = 2
}

public enum GstTaxTreatment
{
    Taxable = 1,
    GstFree = 2,
    InputTaxed = 3,
    BasExcluded = 4
}

public sealed class LedgerAccount
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid LegalEntityId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; set; }
    public LedgerAccountType Type { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ArchivedAt { get; set; }
}

public sealed class LedgerJournal
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid LegalEntityId { get; init; }
    public LedgerJournalType Type { get; init; }
    public LedgerJournalStatus Status { get; set; } = LedgerJournalStatus.Draft;
    public required DateOnly EffectiveOn { get; init; }
    public required string Description { get; init; }
    public Guid CreatedByUserId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? ReversesJournalId { get; init; }
    public Guid? AdjustsJournalId { get; init; }
    public ICollection<LedgerJournalLine> Lines { get; init; } = new List<LedgerJournalLine>();
}

public sealed class LedgerJournalLine
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid LedgerJournalId { get; init; }
    public Guid LedgerAccountId { get; init; }
    public decimal Debit { get; init; }
    public decimal Credit { get; init; }
    public GstTaxTreatment GstTaxTreatment { get; init; }
    public decimal GstRate { get; init; }
}
