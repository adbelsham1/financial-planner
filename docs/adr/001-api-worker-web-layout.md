# ADR-001: Use one App Platform application with API, Worker, and Web components

**Date:** 2026-10-24
**Status:** Accepted

## Context

Financial Planner needs an HTTP API, durable background processing, and a
responsive web interface. It must deploy in DigitalOcean SYD1 but is not a
microservices system.

## Decision

Use one repository and one DigitalOcean App Platform application. Deploy an
ASP.NET Core API, an ASP.NET Core Worker hosting Hangfire, and an Rsbuild
React/MUI static site as separate components. They share a single managed
PostgreSQL database and private Spaces bucket.

## Consequences

The API stays responsive while the Worker handles durable jobs, and each
component can be scaled independently later. The components share deployment
and data boundaries, so this avoids distributed-service coordination. Database
schema changes are applied by the API at startup, as chosen for this initial
production deployment.
