namespace FinancialPlanner.Api.Ledger;

public sealed record CreateLedgerAccountRequest(string Code, string Name, string Type);
public sealed record RenameLedgerAccountRequest(string Name);
public sealed record CreateLedgerJournalRequest(DateOnly EffectiveOn, string Description, IReadOnlyList<LedgerLineRequest?> Lines);
public sealed record CreateOpeningBalanceRequest(
    DateOnly EffectiveOn, string Description, Guid OpeningBalanceEquityAccountId, IReadOnlyList<LedgerLineRequest?> Lines);
public sealed record LedgerLineRequest(Guid AccountId, decimal Debit, decimal Credit, string GstTaxTreatment);
public sealed record CreateAdjustmentRequest(DateOnly EffectiveOn, string Description, IReadOnlyList<LedgerLineRequest?> Lines);
