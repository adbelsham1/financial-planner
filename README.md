# Financial Planner

Financial Planner is an Australian, tenant-isolated financial planning
application for personal finances, SMSFs, joint ownership, assets, liabilities,
and projects.

## Local development

Prerequisites:

- .NET 10 SDK
- Node 22+
- PostgreSQL when exercising readiness, Hangfire, or database-backed features

```bash
dotnet restore FinancialPlanner.slnx
dotnet build FinancialPlanner.slnx
dotnet test FinancialPlanner.slnx

cd financial-planner-web
npm ci
npm run lint
npm run test
VITE_API_BASE_URL=http://localhost:5050/api npm run dev
```

Run the API in a second terminal:

```bash
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=http://localhost:5050 \
dotnet run --project src/FinancialPlanner.Api --no-launch-profile
```

`/api/health/live` confirms process liveness. `/api/health/ready` is healthy
only when `ConnectionStrings__Postgres` is configured and reachable.

## Deployment

Production is DigitalOcean App Platform in SYD1. The API, Worker, and Web
components are defined in `.do/app.yaml`; DigitalOcean setup and required
runtime secrets are documented in `.do/README.md`. GitHub Actions deploys
`main` with `doctl` only after all CI checks pass.
