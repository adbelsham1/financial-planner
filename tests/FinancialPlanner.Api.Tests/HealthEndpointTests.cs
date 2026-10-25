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

    [Fact]
    public async Task Owner_can_manage_joint_entity_ownership_and_contributions()
    {
        using var client = factory.CreateClient();
        await RegisterAndSignInAsync(client);

        var create = await client.PostAsJsonAsync("/api/legal-entities/", new
        {
            name = "Beach House",
            kind = "Joint",
            effectiveFrom = "2026-01-01",
            ownershipParties = new[]
            {
                new { name = "Taylor Owner", percentage = 60m, isWorkspaceOwner = true },
                new { name = "Jordan Co-owner", percentage = 40m, isWorkspaceOwner = false }
            }
        });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var entityId = created.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(100m, created.RootElement.GetProperty("legalOwnershipPercentage").GetDecimal());
        Assert.Equal(60m, created.RootElement.GetProperty("ownerAttributablePercentage").GetDecimal());

        var contribution = await client.PostAsJsonAsync($"/api/legal-entities/{entityId}/contributions", new
        {
            partyName = "Jordan Co-owner",
            effectiveOn = "2026-02-01",
            amount = 12500m
        });
        Assert.Equal(HttpStatusCode.Created, contribution.StatusCode);

        var detail = await client.GetAsync($"/api/legal-entities/{entityId}");
        detail.EnsureSuccessStatusCode();
        using var entity = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal(2, entity.RootElement.GetProperty("ownershipHistory")[0].GetProperty("parties").GetArrayLength());
        Assert.Equal(12500m, entity.RootElement.GetProperty("contributions")[0].GetProperty("amount").GetDecimal());
        Assert.Equal("AUD", entity.RootElement.GetProperty("contributions")[0].GetProperty("currency").GetString());
        var contributionId = entity.RootElement.GetProperty("contributions")[0].GetProperty("id").GetGuid();
        var snapshotId = entity.RootElement.GetProperty("ownershipHistory")[0].GetProperty("id").GetGuid();

        var replacement = await client.PostAsJsonAsync($"/api/legal-entities/{entityId}/ownership-snapshots", new
        {
            effectiveFrom = "2026-01-01",
            supersedesOwnershipSnapshotId = snapshotId,
            parties = new[]
            {
                new { name = "Taylor Owner", percentage = 50m, isWorkspaceOwner = true },
                new { name = "Alex Co-owner", percentage = 50m, isWorkspaceOwner = false }
            }
        });
        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(
            $"/api/legal-entities/{entityId}/contributions/{contributionId}/reverse", new
            {
                effectiveOn = "2026-02-02"
            })).StatusCode);
        var corrected = await client.GetFromJsonAsync<JsonElement>($"/api/legal-entities/{entityId}");
        Assert.Equal(50m, corrected.GetProperty("ownerAttributablePercentage").GetDecimal());
        Assert.Equal(-12500m, corrected.GetProperty("contributions")[0].GetProperty("amount").GetDecimal());
        Assert.Equal(contributionId, corrected.GetProperty("contributions")[0].GetProperty("reversesOwnershipContributionId").GetGuid());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/legal-entities/{entityId}/contributions", new
        {
            partyName = "Jordan Co-owner",
            effectiveOn = "2026-03-01",
            amount = 100m
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/legal-entities/{entityId}/contributions", new
        {
            partyName = "Alex Co-owner",
            effectiveOn = "2026-03-01",
            amount = 0.001m
        })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/legal-entities/{entityId}/archive", null)).StatusCode);
        Assert.True((await client.GetFromJsonAsync<JsonElement>($"/api/legal-entities/{entityId}"))
            .GetProperty("archivedAt").ValueKind != JsonValueKind.Null);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/legal-entities/{entityId}/contributions", new
        {
            partyName = "Alex Co-owner",
            effectiveOn = "2026-03-01",
            amount = 100m
        })).StatusCode);
    }

    [Fact]
    public async Task Joint_ownership_requires_a_complete_allocation_and_is_tenant_scoped()
    {
        using var owner = factory.CreateClient();
        await RegisterAndSignInAsync(owner);
        var invalid = await owner.PostAsJsonAsync("/api/legal-entities/", new
        {
            name = "Invalid joint entity",
            kind = "Joint",
            effectiveFrom = "2026-01-01",
            ownershipParties = new[]
            {
                new { name = "Taylor Owner", percentage = 60m, isWorkspaceOwner = true },
                new { name = "Jordan Co-owner", percentage = 30m, isWorkspaceOwner = false }
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var invalidPrecision = await owner.PostAsJsonAsync("/api/legal-entities/", new
        {
            name = "Invalid precision",
            kind = "Joint",
            effectiveFrom = "2026-01-01",
            ownershipParties = new[]
            {
                new { name = "Taylor Owner", percentage = 33.33335m, isWorkspaceOwner = true },
                new { name = "Jordan Co-owner", percentage = 66.66665m, isWorkspaceOwner = false }
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidPrecision.StatusCode);

        var create = await owner.PostAsJsonAsync("/api/legal-entities/", new
        {
            name = "Private entity",
            kind = "Personal",
            effectiveFrom = (string?)null,
            ownershipParties = (object?)null
        });
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var entityId = created.RootElement.GetProperty("id").GetGuid();

        using var anotherOwner = factory.CreateClient();
        await RegisterAndSignInAsync(anotherOwner);
        Assert.Equal(HttpStatusCode.NotFound, (await anotherOwner.GetAsync($"/api/legal-entities/{entityId}")).StatusCode);
    }

    private static async Task RegisterAndSignInAsync(HttpClient client)
    {
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        const string password = "A-long-enough-password1!";
        var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            displayName = "Taylor Owner",
            email,
            password,
            workspaceName = "Taylor Financial Planner"
        });
        Assert.Equal(HttpStatusCode.Accepted, registration.StatusCode);
        await VerifyOtpAsync(client, "/api/auth/verify-email", email);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/sign-in", new { email, password })).StatusCode);
        await VerifyOtpAsync(client, "/api/auth/sign-in/verify", email);
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
