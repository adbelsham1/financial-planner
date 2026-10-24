using Microsoft.AspNetCore.Mvc.Testing;

namespace FinancialPlanner.Api.Tests;

public sealed class HealthEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Liveness_returns_ok_without_database_configuration()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health/live");

        response.EnsureSuccessStatusCode();
    }
}
