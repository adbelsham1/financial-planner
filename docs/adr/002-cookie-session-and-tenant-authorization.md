# ADR-002: Cookie session and tenant authorization

**Date:** 2026-10-24  
**Status:** Accepted

## Context

The application needs browser authentication that does not expose bearer tokens to JavaScript, while every future financial operation must be constrained to its tenant and legal entity. Email OTPs also require an operational provider without committing delivery credentials.

## Decision

Use short-lived JWT access tokens and opaque, hashed refresh tokens in Secure, HttpOnly, SameSite=Strict cookies. Refresh tokens rotate within a revocable session family; reuse revokes the family. Store OTP hashes only, rate-limit issue and verification attempts, and use a development-only in-memory capture sender. Production validates and uses Gmail SMTP values supplied as runtime secrets.

Evaluate Owner, Editor, Reviewer, and Read-only capability policies from persisted tenant memberships and legal-entity grants on the server. Persist append-only security audit events with a keyed IP hash and a correlation ID.

## Consequences

Browser workflows can authenticate without reading credentials or tokens, and replayed refresh tokens invalidate the affected session. API handlers must apply tenant and legal-entity checks before operating on financial data. Development tests can obtain OTPs from the capture endpoint; that endpoint is not mapped in production. Deployments must provide the JWT, audit-hash, and Gmail secret values.
