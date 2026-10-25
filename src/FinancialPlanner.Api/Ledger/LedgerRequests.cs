using System.Text.Json.Serialization;

namespace FinancialPlanner.Api.Ledger;

public sealed record CreateLedgerAccountRequest(string Code, string Name, string Type);
public sealed record RenameLedgerAccountRequest(string Name);
public sealed record CreateLedgerJournalRequest(DateOnly EffectiveOn, string Description, IReadOnlyList<LedgerLineRequest?> Lines);
public sealed record CreateOpeningBalanceRequest(
    DateOnly EffectiveOn, string Description, Guid OpeningBalanceEquityAccountId, IReadOnlyList<LedgerLineRequest?> Lines);
public sealed record LedgerLineRequest(
    Guid AccountId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Debit,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Credit,
    string GstTaxTreatment);
public sealed record CreateAdjustmentRequest(DateOnly EffectiveOn, string Description, IReadOnlyList<LedgerLineRequest?> Lines);
