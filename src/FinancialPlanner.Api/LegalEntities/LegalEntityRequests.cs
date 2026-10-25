namespace FinancialPlanner.Api.LegalEntities;

public sealed record CreateLegalEntityRequest(
    string Name,
    string Kind,
    DateOnly? EffectiveFrom,
    IReadOnlyList<OwnershipPartyRequest?>? OwnershipParties);

public sealed record OwnershipPartyRequest(string Name, decimal Percentage, bool IsWorkspaceOwner);

public sealed record RenameLegalEntityRequest(string Name);

public sealed record CreateOwnershipSnapshotRequest(
    DateOnly EffectiveFrom,
    IReadOnlyList<OwnershipPartyRequest?> Parties,
    Guid? SupersedesOwnershipSnapshotId);

public sealed record CreateOwnershipContributionRequest(
    string PartyName,
    DateOnly EffectiveOn,
    decimal Amount);

public sealed record ReverseOwnershipContributionRequest(DateOnly EffectiveOn);
