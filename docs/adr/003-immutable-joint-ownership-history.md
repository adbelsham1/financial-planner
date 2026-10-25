# ADR-003: Preserve joint ownership history as immutable snapshots

**Date:** 2026-10-25  
**Status:** Accepted

## Context

Jointly held entities need both their full legal ownership and the workspace
owner's attributable share. Ownership changes and unequal contributions must be
effective-dated and auditable without rewriting historical reports.

## Decision

Store a joint entity's ownership as append-only, full effective-dated snapshots.
Each snapshot contains named parties, exactly one workspace-owner party, and
percentages totalling 100.0000%. A correction links to the immutable snapshot
it supersedes, retaining its original effective date; reporting selects the
latest revision. Store unequal AUD contributions separately as append-only
effective-dated records. Correct a contribution through a linked negative
reversal and, where needed, a replacement positive contribution.

## Consequences

Historical attribution is stable and report calculations can select the latest
revision effective on a reporting date. Creating a joint entity requires a
complete ownership allocation, and users must add linked corrective records
instead of editing prior history.
