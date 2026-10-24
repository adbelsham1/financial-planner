using Microsoft.EntityFrameworkCore;

namespace FinancialPlanner.Infrastructure;

public sealed class PlannerDbContext(DbContextOptions<PlannerDbContext> options) : DbContext(options);
