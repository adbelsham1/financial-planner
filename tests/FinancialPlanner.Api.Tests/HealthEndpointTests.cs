using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

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

    [Fact]
    public async Task Owner_can_activate_sign_in_refresh_and_sign_out()
    {
        using var client = factory.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        const string password = "A-long-enough-password1!";
        var register = await client.PostAsJsonAsync("/api/auth/register", new
        {
            displayName = "Taylor Owner",
            email,
            password,
            workspaceName = "Taylor Financial Planner"
        });
        Assert.Equal(HttpStatusCode.Accepted, register.StatusCode);

        await VerifyOtpAsync(client, "/api/auth/verify-email", email);
        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new { email, password });
        Assert.Equal(HttpStatusCode.Accepted, signIn.StatusCode);
        await VerifyOtpAsync(client, "/api/auth/sign-in/verify", email);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/sign-out", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Otp_is_single_use_and_limited_after_failed_attempts()
    {
        using var client = factory.CreateClient();
        var email = $"otp-{Guid.NewGuid():N}@example.com";
        var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            displayName = "Taylor Owner",
            email,
            password = "A-long-enough-password1!",
            workspaceName = "Taylor Financial Planner"
        });
        Assert.Equal(HttpStatusCode.Accepted, registration.StatusCode);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/verify-email",
                new { email, code = "000000" })).StatusCode);
        }

        var code = await LatestCodeAsync(client, email);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/verify-email",
            new { email, code })).StatusCode);
    }

    private static async Task VerifyOtpAsync(HttpClient client, string path, string email)
    {
        var code = await LatestCodeAsync(client, email);
        var response = await client.PostAsJsonAsync(path, new { email, code });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<string> LatestCodeAsync(HttpClient client, string email)
    {
        var response = await client.GetAsync($"/api/auth/development/emails/{Uri.EscapeDataString(email)}");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString()!;
    }
}
