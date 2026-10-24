namespace FinancialPlanner.Worker;

public class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Financial Planner worker is running.");
        return Task.CompletedTask;
    }
}
