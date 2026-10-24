using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;

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

    [Fact]
    public async Task Registration_creates_an_owner_workspace()
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            displayName = "Taylor Owner",
            email = $"owner-{Guid.NewGuid():N}@example.com",
            password = "A-long-enough-password1!",
            workspaceName = "Taylor Financial Planner"
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }
}
