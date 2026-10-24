using FinancialPlanner.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FinancialPlanner.Api;

public sealed class DatabaseHealthCheck(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Postgres")))
        {
            return HealthCheckResult.Unhealthy("The PostgreSQL connection is not configured.");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();

        return await database.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The PostgreSQL database is unavailable.");
    }
}
