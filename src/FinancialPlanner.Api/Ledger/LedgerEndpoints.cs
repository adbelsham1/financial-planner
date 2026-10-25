using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FinancialPlanner.Api.Auth;
using FinancialPlanner.Infrastructure;
using FinancialPlanner.Infrastructure.Ledger;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinancialPlanner.Api.Ledger;

public static class LedgerEndpoints
{
    public static IEndpointRouteBuilder MapLedgerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/legal-entities/{legalEntityId:guid}/ledger").RequireAuthorization("TenantRead");
        group.MapGet("/accounts", ListAccountsAsync);
        group.MapPost("/accounts", CreateAccountAsync).RequireAuthorization("TenantWrite");
        group.MapPut("/accounts/{accountId:guid}", RenameAccountAsync).RequireAuthorization("TenantWrite");
        group.MapPost("/accounts/{accountId:guid}/archive", ArchiveAccountAsync).RequireAuthorization("TenantWrite");
        group.MapGet("/journals", ListJournalsAsync);
        group.MapPost("/journals", CreateJournalAsync).RequireAuthorization("TenantWrite");
        group.MapPost("/opening-balances", CreateOpeningBalanceAsync).RequireAuthorization("TenantWrite");
        group.MapPost("/journals/{journalId:guid}/approve", ApproveJournalAsync).RequireAuthorization("TenantWrite");
        group.MapPost("/journals/{journalId:guid}/reverse", ReverseJournalAsync).RequireAuthorization("TenantWrite");
        group.MapPost("/journals/{journalId:guid}/adjust", CreateAdjustmentAsync).RequireAuthorization("TenantWrite");
        group.MapGet("/journals/{journalId:guid}/audit", JournalAuditAsync);
        group.MapGet("/trial-balance", TrialBalanceAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAccountsAsync(Guid legalEntityId, ClaimsPrincipal principal,
        LegalEntityAccessService access, PlannerDbContext database, CancellationToken cancellationToken)
    {
        if (!await access.CanAccessAsync(principal, legalEntityId, TenantCapability.Read, cancellationToken)) return Results.NotFound();
        var accounts = await database.LedgerAccounts.AsNoTracking().Where(item => item.LegalEntityId == legalEntityId)
            .OrderBy(item => item.Code).ToListAsync(cancellationToken);
        return Results.Ok(accounts.Select(AccountResponse.From));
    }

    private static async Task<IResult> CreateAccountAsync(Guid legalEntityId, CreateLedgerAccountRequest request,
        ClaimsPrincipal principal, LegalEntityAccessService access, PlannerDbContext database, AuditService audit,
        HttpContext context, CancellationToken cancellationToken)
    {
        if (!TryGetTenant(principal, out var tenantId, out var actorId) ||
            !await access.CanAccessAsync(principal, legalEntityId, TenantCapability.Write, cancellationToken)) return Results.NotFound();
        if (!await IsActiveEntityAsync(legalEntityId, tenantId, database, cancellationToken)) return Results.Conflict(new { detail = "Archived legal entities cannot receive ledger records." });
        if (!TryCreateAccount(request, out var account, tenantId, legalEntityId, out var errors)) return Results.ValidationProblem(errors);
        if (await database.LedgerAccounts.AnyAsync(item => item.LegalEntityId == legalEntityId && item.Code == account.Code, cancellationToken))
            return Results.Conflict(new { detail = "Account codes must be unique within a legal entity." });
        database.LedgerAccounts.Add(account);
        audit.Add(context, "ledger.account-created", $"ledger-account:{account.Id}", "success", tenantId, actorId);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Results.Conflict(new { detail = "Account codes must be unique within a legal entity." });
        }
        return Results.Created($"/api/legal-entities/{legalEntityId}/ledger/accounts/{account.Id}", AccountResponse.From(account));
    }

    private static async Task<IResult> RenameAccountAsync(Guid legalEntityId, Guid accountId, RenameLedgerAccountRequest request,
        ClaimsPrincipal principal, LegalEntityAccessService access, PlannerDbContext database, AuditService audit,
        HttpContext context, CancellationToken cancellationToken)
    {
        if (!TryGetTenant(principal, out var tenantId, out var actorId) ||
            !await access.CanAccessAsync(principal, legalEntityId, TenantCapability.Write, cancellationToken)) return Results.NotFound();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 256)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["An account name of up to 256 characters is required."] });
        var account = await database.LedgerAccounts.SingleOrDefaultAsync(item => item.Id == accountId &&
            item.LegalEntityId == legalEntityId && item.TenantId == tenantId, cancellationToken);
        if (account is null) return Results.NotFound();
        account.Name = request.Name.Trim();
        audit.Add(context, "ledger.account-renamed", $"ledger-account:{account.Id}", "success", tenantId, actorId);
        await database.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ArchiveAccountAsync(Guid legalEntityId, Guid accountId, ClaimsPrincipal principal,
        LegalEntityAccessService access, PlannerDbContext database, AuditService audit, HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenant(principal, out var tenantId, out var actorId) ||
            !await access.CanAccessAsync(principal, legalEntityId, TenantCapability.Write, cancellationToken)) return Results.NotFound();
        var account = await database.LedgerAccounts.SingleOrDefaultAsync(item => item.Id == accountId &&
            item.LegalEntityId == legalEntityId && item.TenantId == tenantId, cancellationToken);
        if (account is null) return Results.NotFound();
        if (account.ArchivedAt is null)
        {
            account.ArchivedAt = DateTimeOffset.UtcNow;
            audit.Add(context, "ledger.account-archived", $"ledger-account:{account.Id}", "success", tenantId, actorId);
            await database.SaveChangesAsync(cancellationToken);
        }
        return Results.NoContent();
    }

    private static async Task<IResult> CreateJournalAsync(Guid legalEntityId, CreateLedgerJournalRequest request,
        ClaimsPrincipal principal, LegalEntityAccessService access, PlannerDbContext database, AuditService audit,
        HttpContext context, CancellationToken cancellationToken) =>
        await CreateDraftAsync(legalEntityId, request.EffectiveOn, request.Description, request.Lines, LedgerJournalType.Manual,
            null, null, null, false, principal, access, database, audit, context, cancellationToken);

    private static async Task<IResult> CreateOpeningBalanceAsync(Guid legalEntityId, CreateOpeningBalanceRequest request,
        ClaimsPrincipal principal, LegalEntityAccessService access, PlannerDbContext database, AuditService audit,
        HttpContext context, CancellationToken cancellationToken)
    {
        if (!TryGetTenant(principal, out var tenantId, out _) ||
            !await access.CanAccessAsync(principal, legalEntityId, TenantCapability.Write, cancellationToken)) return Results.NotFound();
        var equity = await database.LedgerAccounts.SingleOrDefaultAsync(item => item.Id == request.OpeningBalanceEquityAccountId &&
            item.TenantId == tenantId && item.LegalEntityId == legalEntityId && item.Type == LedgerAccountType.Equity &&
            item.ArchivedAt == null, cancellationToken);
        if (request.Lines is null || equity is null || !request.Lines.Where(item => item is not null).Any(item => item!.AccountId == equity.Id))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["openingBalanceEquityAccountId"] = ["An active equity account included in the opening balance is required."] });
        return await CreateDraftAsync(legalEntityId, request.EffectiveOn, request.Description, request.Lines,
            LedgerJournalType.OpeningBalance, null, null, null, false, principal, access, database, audit, context, cancellationToken);
    }

    private static async Task<IResult> ApproveJournalAsync(Guid legalEntityId, Guid journalId, ClaimsPrincipal principal,
        LegalEntityAccessService access, PlannerDbContext database, AuditService audit, HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenant(principal, out var tenantId, out var actorId) ||
            !await access.CanAccessAsync(principal, legalEntityId, TenantCapability.Write, cancellationToken)) return Results.NotFound();
        if (!await IsActiveEntityAsync(legalEntityId, tenantId, database, cancellationToken))
            return Results.Conflict(new { detail = "Archived legal entities cannot receive ledger records." });
        var journal = await database.LedgerJournals.Include(item => item.Lines).SingleOrDefaultAsync(item =>
            item.Id == journalId && item.TenantId == tenantId && item.LegalEntityId == legalEntityId, cancellationToken);
        if (journal is null) return Results.NotFound();
        if (journal.Status == LedgerJournalStatus.Approved) return Results.Conflict(new { detail = "The journal is already approved." });
        if (!IsBalanced(journal.Lines)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["lines"] = ["Journal debits and credits must balance."] });
        journal.Status = LedgerJournalStatus.Approved;
        journal.ApprovedByUserId = actorId;
        journal.ApprovedAt = DateTimeOffset.UtcNow;
        audit.Add(context, "ledger.journal-approved", $"ledger-journal:{journal.Id}", "success", tenantId, actorId);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { detail = "The journal was approved by another request." });
        }
        return Results.Ok(await ToJournalResponseAsync(journal, database, cancellationToken));
    }

    private static async Task<IResult> ReverseJournalAsync(Guid legalEntityId, Guid journalId, CreateAdjustmentRequest request,
        ClaimsPrincipal principal, LegalEntityAccessService access, PlannerDbContext database, AuditService audit,
        HttpContext context, CancellationToken cancellationToken)
    {
        if (!TryGetTenant(principal, out var tenantId, out _) ||
            !await access.CanAccessAsync(principal, legalEntityId, TenantCapability.Write, cancellationToken)) return Results.NotFound();
        var original = await database.LedgerJournals.Include(item => item.Lines).SingleOrDefaultAsync(item => item.Id == journalId &&
            item.TenantId == tenantId && item.LegalEntityId == legalEntityId && item.Status == LedgerJournalStatus.Approved, cancellationToken);
        if (original is null) return Results.NotFound();
        if (await database.LedgerJournals.AnyAsync(item => item.ReversesJournalId == original.Id, cancellationToken))
            return Results.Conflict(new { detail = "This journal has already been reversed." });
        var reversedLines = original.Lines.Select(item => new LedgerLineRequest(item.LedgerAccountId, item.Credit, item.Debit,
            item.GstTaxTreatment.ToString())).Cast<LedgerLineRequest?>().ToList();
        return await CreateDraftAsync(legalEntityId, request.EffectiveOn, request.Description, reversedLines,
            LedgerJournalType.Manual, original.Id, null, original, true, principal, access, database, audit, context, cancellationToken);
    }

    private static async Task<IResult> CreateAdjustmentAsync(Guid legalEntityId, Guid journalId, CreateAdjustmentRequest request,
        ClaimsPrincipal principal, LegalEntityAccessService access, PlannerDbContext database, AuditService audit,
        HttpContext context, CancellationToken cancellationToken)
    {
        if (!TryGetTenant(principal, out var tenantId, out _) ||
            !await access.CanAccessAsync(principal, legalEntityId, TenantCapability.Write, cancellationToken)) return Results.NotFound();
        var original = await database.LedgerJournals.SingleOrDefaultAsync(item => item.Id == journalId && item.TenantId == tenantId &&
            item.LegalEntityId == legalEntityId && item.Status == LedgerJournalStatus.Approved, cancellationToken);
        if (original is null) return Results.NotFound();
        return await CreateDraftAsync(legalEntityId, request.EffectiveOn, request.Description, request.Lines,
            LedgerJournalType.Manual, null, original.Id, original, false, principal, access, database, audit, context, cancellationToken);
    }

    private static async Task<IResult> ListJournalsAsync(Guid legalEntityId, ClaimsPrincipal principal,
        LegalEntityAccessService access, PlannerDbContext database, CancellationToken cancellationToken)
    {
        if (!await access.CanAccessAsync(principal, legalEntityId, TenantCapability.Read, cancellationToken)) return Results.NotFound();
        var journals = await database.LedgerJournals.AsNoTracking().Include(item => item.Lines)
            .Where(item => item.LegalEntityId == legalEntityId).OrderByDescending(item => item.EffectiveOn).ToListAsync(cancellationToken);
        return Results.Ok(await ToJournalResponsesAsync(journals, database, cancellationToken));
    }

    private static async Task<IResult> JournalAuditAsync(Guid legalEntityId, Guid journalId, ClaimsPrincipal principal,
        LegalEntityAccessService access, PlannerDbContext database, CancellationToken cancellationToken)
    {
        if (!await access.CanAccessAsync(principal, legalEntityId, TenantCapability.Read, cancellationToken)) return Results.NotFound();
        var exists = await database.LedgerJournals.AnyAsync(item => item.Id == journalId && item.LegalEntityId == legalEntityId, cancellationToken);
        if (!exists) return Results.NotFound();
        var events = await database.AuditEvents.AsNoTracking().Where(item => item.Target == $"ledger-journal:{journalId}")
            .OrderBy(item => item.OccurredAt).Select(item => new AuditResponse(item.Action, item.ActorId, item.OccurredAt, item.Outcome))
            .ToListAsync(cancellationToken);
        return Results.Ok(events);
    }

    private static async Task<IResult> TrialBalanceAsync(Guid legalEntityId, DateOnly asOf, ClaimsPrincipal principal,
        LegalEntityAccessService access, PlannerDbContext database, CancellationToken cancellationToken)
    {
        if (!await access.CanAccessAsync(principal, legalEntityId, TenantCapability.Read, cancellationToken)) return Results.NotFound();
        var totals = await database.LedgerJournalLines.AsNoTracking().Where(line =>
            database.LedgerJournals.Any(journal => journal.Id == line.LedgerJournalId && journal.LegalEntityId == legalEntityId &&
                journal.Status == LedgerJournalStatus.Approved && journal.EffectiveOn <= asOf))
            .GroupBy(line => line.LedgerAccountId)
            .Select(group => new TrialBalanceTotal(group.Key, group.Sum(line => line.Debit), group.Sum(line => line.Credit)))
            .ToDictionaryAsync(item => item.AccountId, cancellationToken);
        var accounts = await database.LedgerAccounts.AsNoTracking().Where(item => item.LegalEntityId == legalEntityId).ToListAsync(cancellationToken);
        var rows = accounts.Select(account =>
        {
            var net = totals.TryGetValue(account.Id, out var total) ? total.Debit - total.Credit : 0m;
            return new TrialBalanceRow(account.Id, account.Code, account.Name, account.Type.ToString(),
                net > 0 ? net : 0, net < 0 ? -net : 0);
        })
            .Where(item => item.Debit != 0 || item.Credit != 0).OrderBy(item => item.Code).ToList();
        return Results.Ok(new TrialBalanceResponse(asOf, rows, rows.Sum(item => item.Debit), rows.Sum(item => item.Credit)));
    }

    private static async Task<IResult> CreateDraftAsync(Guid legalEntityId, DateOnly effectiveOn, string description,
        IReadOnlyList<LedgerLineRequest?>? lines, LedgerJournalType type, Guid? reversesJournalId, Guid? adjustsJournalId,
        LedgerJournal? original, bool allowArchivedAccounts, ClaimsPrincipal principal, LegalEntityAccessService access, PlannerDbContext database,
        AuditService audit, HttpContext context, CancellationToken cancellationToken)
    {
        if (!TryGetTenant(principal, out var tenantId, out var actorId) ||
            !await access.CanAccessAsync(principal, legalEntityId, TenantCapability.Write, cancellationToken)) return Results.NotFound();
        if (!await IsActiveEntityAsync(legalEntityId, tenantId, database, cancellationToken)) return Results.Conflict(new { detail = "Archived legal entities cannot receive ledger records." });
        if (!TryCreateLines(lines, out var draftLines, out var errors) || string.IsNullOrWhiteSpace(description) || description.Trim().Length > 512 || effectiveOn == DateOnly.MinValue)
        {
            if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 512) errors["description"] = ["A description of up to 512 characters is required."];
            if (effectiveOn == DateOnly.MinValue) errors["effectiveOn"] = ["An effective date is required."];
            return Results.ValidationProblem(errors);
        }
        var accountIds = draftLines.Select(item => item.LedgerAccountId).Distinct().ToList();
        var validAccountCount = await database.LedgerAccounts.CountAsync(item => item.LegalEntityId == legalEntityId &&
            item.TenantId == tenantId && (allowArchivedAccounts || item.ArchivedAt == null) && accountIds.Contains(item.Id), cancellationToken);
        if (validAccountCount != accountIds.Count)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["lines"] = ["Every line must use an active account in this legal entity."] });
        var journal = new LedgerJournal
        {
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            Type = type,
            Currency = "AUD",
            EffectiveOn = effectiveOn,
            Description = description.Trim(),
            CreatedByUserId = actorId,
            ReversesJournalId = reversesJournalId,
            AdjustsJournalId = adjustsJournalId,
            Lines = draftLines
        };
        database.LedgerJournals.Add(journal);
        audit.Add(context, original is null ? "ledger.journal-created" : reversesJournalId is not null ? "ledger.journal-reversal-created" :
            "ledger.journal-adjustment-created", $"ledger-journal:{journal.Id}", "success", tenantId, actorId);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (reversesJournalId is not null && IsUniqueViolation(exception))
        {
            return Results.Conflict(new { detail = "This journal has already been reversed." });
        }
        return Results.Created($"/api/legal-entities/{legalEntityId}/ledger/journals/{journal.Id}",
            await ToJournalResponseAsync(journal, database, cancellationToken));
    }

    private static bool TryCreateAccount(CreateLedgerAccountRequest request, out LedgerAccount account, Guid tenantId,
        Guid legalEntityId, out Dictionary<string, string[]> errors)
    {
        errors = [];
        account = null!;
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 32) errors["code"] = ["An account code of up to 32 characters is required."];
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 256) errors["name"] = ["An account name of up to 256 characters is required."];
        if (!Enum.TryParse<LedgerAccountType>(request.Type, true, out var type) || !Enum.IsDefined(type)) errors["type"] = ["An asset, liability, equity, income, or expense type is required."];
        if (errors.Count > 0) return false;
        account = new LedgerAccount { TenantId = tenantId, LegalEntityId = legalEntityId, Code = request.Code.Trim().ToUpperInvariant(), Name = request.Name.Trim(), Type = type };
        return true;
    }

    private static bool TryCreateLines(IReadOnlyList<LedgerLineRequest?>? supplied, out List<LedgerJournalLine> lines,
        out Dictionary<string, string[]> errors)
    {
        lines = []; errors = [];
        if (supplied is null || supplied.Count < 2 || supplied.Any(item => item is null))
        {
            errors["lines"] = ["At least two journal lines are required."];
            return false;
        }
        foreach (var item in supplied.Cast<LedgerLineRequest>())
        {
            if (item.AccountId == Guid.Empty || item.Debit < 0 || item.Credit < 0 || (item.Debit == 0) == (item.Credit == 0) ||
                item.Debit > 9_999_999_999_999_999.99m || item.Credit > 9_999_999_999_999_999.99m ||
                !HasAtMostScale(item.Debit, 2) || !HasAtMostScale(item.Credit, 2) ||
                !Enum.TryParse<GstTaxTreatment>(item.GstTaxTreatment, true, out var treatment) || !Enum.IsDefined(treatment))
            {
                errors["lines"] = ["Each line requires one positive debit or credit amount with two decimal places and a valid GST/tax treatment."];
                return false;
            }
            lines.Add(new LedgerJournalLine
            {
                LedgerAccountId = item.AccountId,
                Debit = item.Debit,
                Credit = item.Credit,
                GstTaxTreatment = treatment,
                GstRate = treatment == GstTaxTreatment.Taxable ? 0.10m : 0m
            });
        }
        if (!IsBalanced(lines)) errors["lines"] = ["Journal debits and credits must balance."];
        return errors.Count == 0;
    }

    private static bool IsBalanced(IEnumerable<LedgerJournalLine> lines) => lines.Sum(item => item.Debit) == lines.Sum(item => item.Credit);
    private static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    private static bool HasAtMostScale(decimal value, int scale) => value == decimal.Round(value, scale, MidpointRounding.ToZero);
    private static async Task<bool> IsActiveEntityAsync(Guid id, Guid tenantId, PlannerDbContext database, CancellationToken token) =>
        await database.LegalEntities.AnyAsync(item => item.Id == id && item.TenantId == tenantId && item.ArchivedAt == null, token);
    private static bool TryGetTenant(ClaimsPrincipal principal, out Guid tenantId, out Guid actorId)
    {
        tenantId = Guid.Empty;
        actorId = Guid.Empty;
        return Guid.TryParse(principal.FindFirstValue("tenant_id"), out tenantId) &&
            Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
                principal.FindFirstValue(ClaimTypes.NameIdentifier), out actorId);
    }

    private static async Task<JournalResponse> ToJournalResponseAsync(LedgerJournal journal, PlannerDbContext database, CancellationToken token) =>
        (await ToJournalResponsesAsync([journal], database, token)).Single();
    private static async Task<IReadOnlyList<JournalResponse>> ToJournalResponsesAsync(IEnumerable<LedgerJournal> journals,
        PlannerDbContext database, CancellationToken token)
    {
        var list = journals.ToList();
        var accountIds = list.SelectMany(item => item.Lines).Select(item => item.LedgerAccountId).Distinct().ToList();
        var accounts = await database.LedgerAccounts.AsNoTracking().Where(item => accountIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, token);
        return list.Select(journal => new JournalResponse(journal.Id, journal.Type.ToString(), journal.Status.ToString(), journal.Currency, journal.EffectiveOn,
            journal.Description, journal.CreatedByUserId, journal.CreatedAt, journal.ApprovedByUserId, journal.ApprovedAt,
            journal.ReversesJournalId, journal.AdjustsJournalId, journal.Lines.Select(line => new JournalLineResponse(line.Id,
                line.LedgerAccountId, accounts[line.LedgerAccountId].Code, accounts[line.LedgerAccountId].Name, line.Debit, line.Credit,
                line.GstTaxTreatment.ToString(), line.GstRate)).ToList())).ToList();
    }

    private sealed record AccountResponse(Guid Id, string Code, string Name, string Type, DateTimeOffset CreatedAt, DateTimeOffset? ArchivedAt)
    {
        public static AccountResponse From(LedgerAccount account) => new(account.Id, account.Code, account.Name, account.Type.ToString(), account.CreatedAt, account.ArchivedAt);
    }
    private sealed record JournalResponse(Guid Id, string Type, string Status, string Currency, DateOnly EffectiveOn, string Description, Guid CreatedByUserId,
        DateTimeOffset CreatedAt, Guid? ApprovedByUserId, DateTimeOffset? ApprovedAt, Guid? ReversesJournalId, Guid? AdjustsJournalId,
        IReadOnlyList<JournalLineResponse> Lines);
    private sealed record JournalLineResponse(Guid Id, Guid AccountId, string AccountCode, string AccountName, decimal Debit, decimal Credit,
        string GstTaxTreatment, decimal GstRate);
    private sealed record AuditResponse(string Action, Guid? ActorId, DateTimeOffset OccurredAt, string Outcome);
    private sealed record TrialBalanceRow(Guid AccountId, string Code, string Name, string Type, decimal Debit, decimal Credit);
    private sealed record TrialBalanceTotal(Guid AccountId, decimal Debit, decimal Credit);
    private sealed record TrialBalanceResponse(DateOnly AsOf, IReadOnlyList<TrialBalanceRow> Accounts, decimal TotalDebits, decimal TotalCredits);
}
