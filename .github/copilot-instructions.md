# GitHub Copilot Instructions

## Code Change Workflow

For every **non-trivial** code change request (skip for typos, dependency bumps, and formatting):

1. **Grill** — invoke the `grill-me` skill to clarify the requirement before acting.
2. **Issue** — create or use a GitHub issue with clarified acceptance criteria. Add it to the Financial Planner Delivery project.
3. **Branch** — create `topic/{issue-number}-{short-slug}` from the current default branch.
4. **Implement** — make the smallest complete vertical-slice change. Write an ADR for any consequential decision.
5. **Test** — add automated tests for the issue acceptance criteria. User-facing workflows require Playwright end-to-end coverage of their happy path and relevant negative cases.
6. **PR** — raise a pull request with `Closes #{issue-number}`, a concise summary, tests run, and any ADRs written.

> **Rule: never close an issue manually.** A merged pull request containing `Closes #{issue-number}` closes it.

### ADR convention

Consequential decisions—anything non-trivial to reverse—require an ADR:

- Directory: `docs/adr/`
- Filename: `{NNN}-{short-slug}.md`, zero-padded (for example, `001-tenant-isolation.md`)
- Template: `docs/adr/TEMPLATE.md`

## Product Context

Financial Planner is an Australian, multi-tenant financial planning application. The first tenant is owner-operated, but all data access must be tenant-scoped so that future customers and collaborators cannot access one another's data.

The product is the primary, auditable bookkeeping ledger for:

- Personal/household finances, SMSF finances, and joint ownership.
- Assets, liabilities, valuations, income, bills, invoices, commitments, and projects.
- Owner-build projects, including construction in progress, approved capitalization, budgets, actuals, and schedules.
- Australian GST/tax classification and accountant-ready records; it does not provide tax, legal, financial, or investment advice and does not lodge returns.

### Core invariants

- Every financial record belongs to a tenant and legal entity. Enforce authorization server-side; never rely on client filtering.
- SMSF data is a separate legal entity with separate ledger, accounts, documents, assets, liabilities, and permissions.
- Approved ledger entries, imports, valuations, and source documents are immutable. Correct them through linked reversals, adjustments, or superseding records—not silent mutation or deletion.
- Maintain a complete actor/time/source audit trail for financial and security-sensitive actions.
- Imported statements must be source-traceable, deduplicated, and reconciled before they feed finalized reports.
- OCR, document extraction, transaction classification, and cost research produce reviewable suggestions only. They must not silently post entries, alter budgets, contact suppliers, purchase goods, or initiate payments.
- Financial email notifications must not contain account balances, transaction details, or other sensitive financial data.
- Preserve both full legal ownership and attributable ownership for joint assets and liabilities. Ownership changes and unequal contributions are effective-dated.
- All money values use AUD at launch. Store enough metadata to support foreign currencies later without treating them as supported now.

### Scope boundaries

- Bank integrations are read-only. The application does not initiate payments, transfers, loan applications, or supplier outreach.
- Gmail document intake uses a dedicated account. Retain originals and require review before creating financial records.
- Public-web cost research uses permitted sources, retains citations and assumptions, and remains evidence-only.
- External AI processing is opt-in. Do not send documents or financial data to external AI providers without explicit user consent.
- Application data and document storage must remain in Australia. Email delivery may use Gmail SMTP, with minimized non-sensitive message content.

## Target Architecture

The intended stack is:

| Layer | Technology |
|---|---|
| Client | React, TypeScript, MUI, responsive PWA |
| API | ASP.NET Core |
| Data | PostgreSQL via EF Core |
| Background jobs | Hangfire backed by PostgreSQL |
| Hosting | DigitalOcean App Platform in SYD1 |
| Object storage | Encrypted DigitalOcean Spaces in SYD1 |
| Deployment | `doctl` and DigitalOcean App Platform |

The repository is being bootstrapped. Issue #1 establishes the solution structure, test commands, CI, and deployment configuration. Do not add a different framework, cloud provider, payment capability, or external data processor without an ADR and an approved issue.

### Intended application boundaries

- Keep domain models and accounting rules independent of HTTP, UI, storage, and third-party providers.
- Treat imports, Gmail intake, OCR, research, notifications, and backup checks as durable background jobs. Jobs must be idempotent and audit their outcomes.
- Use explicit API contracts and validation. Return standard problem details for failures; never hide, broadly catch, or convert invalid financial input into success-shaped results.
- Store documents separately from structured financial metadata and retain an immutable source reference from the record that uses them.
- Model ledger postings as balanced entries. Validate debits equal credits before approval.

## Testing

Use the test commands established by issue #1. Until then, do not claim a build, lint, unit test, integration test, or end-to-end suite passed.

- Test domain/accounting rules with unit tests.
- Test persistence, authorization, imports, reconciliation, and background-job behavior with integration tests against PostgreSQL.
- Test user workflows with Playwright against the running API and a real test database.
- Do not intercept or mock API responses in Playwright tests. Seed real test data through supported fixtures, APIs, or database helpers.
- Cover the acceptance criteria from the linked issue, including relevant permission and invalid-input cases.

## Deployment and Operations

- Deploy through `doctl` to DigitalOcean App Platform in SYD1.
- Keep credentials, connection strings, OAuth tokens, SMTP credentials, and encryption material out of source control and logs.
- Use environment-specific configuration and explicit secret bindings. Never commit a `.env` file containing working credentials.
- Production database changes require reviewed, forward-compatible migrations and rollback/recovery consideration.
- Maintain daily encrypted backups with 90-day retention and test restoration quarterly.
