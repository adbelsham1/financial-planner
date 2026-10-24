using FinancialPlanner.Api;
using FinancialPlanner.Infrastructure;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
var isProduction = builder.Environment.IsProduction();
var connectionString = builder.Configuration.GetConnectionString("Postgres");

if (isProduction && string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:Postgres must be configured in production.");
}

if (isProduction)
{
    builder.Services
        .AddOptions<InfrastructureOptions>()
        .Bind(builder.Configuration.GetSection(InfrastructureOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();
}

builder.Services.AddControllers();
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<DatabaseHealthCheck>("postgres", tags: ["ready"]);

if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddPlannerPersistence(connectionString);
    builder.Services.AddHangfire(configuration => configuration.UsePostgreSqlStorage(
        options => options.UseNpgsqlConnection(connectionString)));
}

var webOrigin = builder.Configuration["Infrastructure:WebOrigin"];
if (!string.IsNullOrWhiteSpace(webOrigin))
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
        policy.WithOrigins(webOrigin).AllowAnyHeader().AllowAnyMethod()));
}

var app = builder.Build();

if (!string.IsNullOrWhiteSpace(connectionString) && isProduction)
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<PlannerDbContext>().Database.MigrateAsync();
}

app.UseCors();
app.MapHealthChecks("/api/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live")
});
app.MapHealthChecks("/api/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
app.MapControllers();

app.Run();

public partial class Program;
