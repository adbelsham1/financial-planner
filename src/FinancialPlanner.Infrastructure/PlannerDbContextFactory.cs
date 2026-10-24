using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FinancialPlanner.Infrastructure;

public sealed class PlannerDbContextFactory : IDesignTimeDbContextFactory<PlannerDbContext>
{
    public PlannerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql("Host=localhost;Database=financial_planner_design;Username=postgres;Password=postgres")
            .Options;
        return new PlannerDbContext(options);
    }
}
