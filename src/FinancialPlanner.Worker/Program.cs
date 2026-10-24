using FinancialPlanner.Worker;
using FinancialPlanner.Infrastructure;
using Hangfire;
using Hangfire.PostgreSql;

var builder = Host.CreateApplicationBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Postgres");

if (builder.Environment.IsProduction() && string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:Postgres must be configured in production.");
}

if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddPlannerPersistence(connectionString);
    builder.Services.AddHangfire(configuration => configuration.UsePostgreSqlStorage(
        options => options.UseNpgsqlConnection(connectionString)));
    builder.Services.AddHangfireServer();
}

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
