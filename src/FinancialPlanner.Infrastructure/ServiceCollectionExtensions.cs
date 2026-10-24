using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialPlanner.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPlannerPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<PlannerDbContext>(options => options.UseNpgsql(connectionString));

        return services;
    }
}
