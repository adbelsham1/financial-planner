# ADR-004: Immutable ledger postings

**Date:** 2026-10-25  
**Status:** Accepted

## Context

The ledger must preserve approved financial history, including per-line Australian GST/tax treatment, while allowing corrections and opening balances. Mutable posted journals or account deletion would undermine auditability and make selected-date trial balances unreliable.

## Decision

Model opening balances and manual journals as balanced draft postings. Owners and Editors approve drafts, after which journals and their lines are append-only. Corrections create separately approved linked reversal or adjustment journals. Store GST/tax treatment and its rate snapshot on every line. Keep a five-type, legal-entity-scoped chart of accounts; codes and types are immutable after creation, while accounts can be renamed or archived.

## Consequences

Approved records can be traced through linked corrections and request audit events without changing prior financial facts. Opening balances use the same controls as all other postings. Draft creation and approval add workflow steps, and an opening balance requires the user to select an existing equity account.
