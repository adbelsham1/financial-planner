using FinancialPlanner.Worker;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialPlanner.Worker.Tests;

public sealed class WorkerTests
{
    [Fact]
    public void Worker_can_be_constructed()
    {
        var worker = new Worker(NullLogger<Worker>.Instance);

        Assert.NotNull(worker);
    }
}
