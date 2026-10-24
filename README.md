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
PUBLIC_API_BASE_URL=http://localhost:5050/api npm run dev
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
# Financial Planner

## Authentication end-to-end tests

Start the real PostgreSQL test database with `docker compose -f docker-compose.e2e.yml up -d`, then start the API with
`ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5050 ConnectionStrings__Postgres='Host=127.0.0.1;Database=financial_planner_e2e;Username=postgres;Password=postgres' Infrastructure__WebOrigin=http://127.0.0.1:3000 dotnet run --project src/FinancialPlanner.Api`.
Run `npm run test:e2e` in `financial-planner-web`. Development-only email capture supplies test OTPs; production uses Gmail SMTP settings exclusively from runtime secrets.
