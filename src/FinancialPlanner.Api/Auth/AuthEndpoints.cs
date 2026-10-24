using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FinancialPlanner.Api.Email;
using FinancialPlanner.Infrastructure;
using FinancialPlanner.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace FinancialPlanner.Api.Auth;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth");
        group.MapPost("/register", RegisterAsync);
        group.MapPost("/verify-email", VerifyEmailAsync);
        group.MapPost("/sign-in", BeginSignInAsync);
        group.MapPost("/sign-in/verify", CompleteSignInAsync);
        group.MapPost("/refresh", RefreshAsync);
        group.MapPost("/sign-out", SignOutAsync);
        group.MapPost("/password-reset", RequestPasswordResetAsync);
        group.MapPost("/password-reset/verify", CompletePasswordResetAsync);
        group.MapGet("/me", GetCurrentUserAsync).RequireAuthorization();
        return group;
    }

    private static async Task<IResult> RequestPasswordResetAsync(
        ResetPasswordRequest request,
        UserManager<AppUser> users,
        PlannerDbContext database,
        IAuthenticationEmailSender emailSender,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.EmailConfirmed)
        {
            return Results.Accepted();
        }

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        database.EmailOtps.Add(new EmailOtp
        {
            UserId = user.Id,
            Purpose = "password-reset",
            CodeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        });
        await database.SaveChangesAsync(cancellationToken);
        await emailSender.SendOtpAsync(user.Email!, code, cancellationToken);
        return Results.Accepted();
    }

    private static async Task<IResult> CompletePasswordResetAsync(
        CompletePasswordResetRequest request,
        UserManager<AppUser> users,
        PlannerDbContext database,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return Results.BadRequest();
        }

        var codeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Code)));
        var otp = await database.EmailOtps.OrderByDescending(item => item.ExpiresAt)
            .FirstOrDefaultAsync(item => item.UserId == user.Id
                && item.Purpose == "password-reset"
                && item.ConsumedAt == null
                && item.ExpiresAt > DateTimeOffset.UtcNow, cancellationToken);
        if (otp is null || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(otp.CodeHash), Convert.FromHexString(codeHash)))
        {
            return Results.BadRequest();
        }

        var resetToken = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, resetToken, request.NewPassword);
        if (!result.Succeeded)
        {
            return Results.ValidationProblem(result.Errors
                .GroupBy(error => error.Code)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));
        }

        otp.ConsumedAt = DateTimeOffset.UtcNow;
        await database.RefreshTokens.Where(token => token.UserId == user.Id && token.RevokedAt == null)
            .ExecuteUpdateAsync(updates => updates.SetProperty(token => token.RevokedAt, DateTimeOffset.UtcNow), cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> RefreshAsync(
        SessionService sessions,
        HttpContext context,
        CancellationToken cancellationToken) =>
        await sessions.RefreshAsync(context, cancellationToken) ? Results.NoContent() : Results.Unauthorized();

    private static async Task<IResult> SignOutAsync(
        PlannerDbContext database,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (context.Request.Cookies.TryGetValue("fp_refresh", out var refreshToken))
        {
            var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
            await database.RefreshTokens
                .Where(token => token.TokenHash == tokenHash && token.RevokedAt == null)
                .ExecuteUpdateAsync(updates => updates.SetProperty(token => token.RevokedAt, DateTimeOffset.UtcNow),
                    cancellationToken);
        }

        context.Response.Cookies.Delete("fp_access");
        context.Response.Cookies.Delete("fp_refresh");
        return Results.NoContent();
    }

    [Authorize]
    private static async Task<IResult> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        PlannerDbContext database,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var membership = await database.TenantMemberships
            .SingleAsync(item => item.UserId == userId, cancellationToken);
        return Results.Ok(new { membership.TenantId, UserId = userId });
    }

    private static async Task<IResult> BeginSignInAsync(
        SignInRequest request,
        UserManager<AppUser> users,
        SignInManager<AppUser> signInManager,
        PlannerDbContext database,
        IAuthenticationEmailSender emailSender,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.EmailConfirmed)
        {
            return Results.Unauthorized();
        }

        var passwordResult = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!passwordResult.Succeeded)
        {
            return Results.Unauthorized();
        }

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        database.EmailOtps.Add(new EmailOtp
        {
            UserId = user.Id,
            Purpose = "sign-in",
            CodeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        });
        database.AuditEvents.Add(new AuditEvent
        {
            ActorId = user.Id,
            Action = "authentication.password-verified",
            Outcome = "success",
            CorrelationId = Guid.NewGuid().ToString("N")
        });
        await database.SaveChangesAsync(cancellationToken);
        await emailSender.SendOtpAsync(user.Email!, code, cancellationToken);
        return Results.Accepted();
    }

    private static async Task<IResult> CompleteSignInAsync(
        VerifyOtpRequest request,
        UserManager<AppUser> users,
        PlannerDbContext database,
        SessionService sessions,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return Results.BadRequest();
        }

        var codeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Code)));
        var otp = await database.EmailOtps
            .OrderByDescending(item => item.ExpiresAt)
            .FirstOrDefaultAsync(item => item.UserId == user.Id
                && item.Purpose == "sign-in"
                && item.ConsumedAt == null
                && item.ExpiresAt > DateTimeOffset.UtcNow, cancellationToken);
        if (otp is null || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(otp.CodeHash),
                Convert.FromHexString(codeHash)))
        {
            return Results.BadRequest();
        }

        var membership = await database.TenantMemberships
            .SingleAsync(item => item.UserId == user.Id, cancellationToken);
        otp.ConsumedAt = DateTimeOffset.UtcNow;
        database.AuditEvents.Add(new AuditEvent
        {
            TenantId = membership.TenantId,
            ActorId = user.Id,
            Action = "authentication.sign-in",
            Outcome = "success",
            CorrelationId = context.TraceIdentifier
        });
        await database.SaveChangesAsync(cancellationToken);
        await sessions.IssueAsync(user, membership.TenantId, context, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> RegisterAsync(
        RegisterOwnerRequest request,
        UserManager<AppUser> users,
        PlannerDbContext database,
        IAuthenticationEmailSender emailSender,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        if (await users.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            return Results.Conflict();
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim(),
            EmailConfirmed = false
        };
        var identityResult = await users.CreateAsync(user, request.Password);
        if (!identityResult.Succeeded)
        {
            return Results.ValidationProblem(identityResult.Errors
                .GroupBy(error => error.Code)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));
        }

        var tenant = new Tenant { Name = request.WorkspaceName.Trim() };
        var legalEntity = new LegalEntity
        {
            TenantId = tenant.Id,
            Name = $"{user.DisplayName} Personal",
            Kind = LegalEntityKind.Personal
        };
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        database.Tenants.Add(tenant);
        database.LegalEntities.Add(legalEntity);
        database.TenantMemberships.Add(new TenantMembership
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Role = TenantRole.Owner
        });
        database.LegalEntityGrants.Add(new LegalEntityGrant
        {
            LegalEntityId = legalEntity.Id,
            UserId = user.Id
        });
        database.EmailOtps.Add(new EmailOtp
        {
            UserId = user.Id,
            Purpose = "email-confirmation",
            CodeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        });
        database.AuditEvents.Add(new AuditEvent
        {
            TenantId = tenant.Id,
            ActorId = user.Id,
            Action = "owner.registration.requested",
            Outcome = "success",
            CorrelationId = Guid.NewGuid().ToString("N")
        });
        await database.SaveChangesAsync(cancellationToken);
        await emailSender.SendOtpAsync(user.Email, code, cancellationToken);

        return Results.Accepted();
    }

    private static async Task<IResult> VerifyEmailAsync(
        VerifyOtpRequest request,
        UserManager<AppUser> users,
        PlannerDbContext database,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return Results.BadRequest();
        }

        var codeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Code)));
        var otp = await database.EmailOtps
            .OrderByDescending(item => item.ExpiresAt)
            .FirstOrDefaultAsync(item => item.UserId == user.Id
                && item.Purpose == "email-confirmation"
                && item.ConsumedAt == null
                && item.ExpiresAt > DateTimeOffset.UtcNow, cancellationToken);
        if (otp is null || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(otp.CodeHash),
                Convert.FromHexString(codeHash)))
        {
            return Results.BadRequest();
        }

        otp.ConsumedAt = DateTimeOffset.UtcNow;
        user.EmailConfirmed = true;
        database.AuditEvents.Add(new AuditEvent
        {
            ActorId = user.Id,
            Action = "owner.email-confirmed",
            Outcome = "success",
            CorrelationId = Guid.NewGuid().ToString("N")
        });
        await database.SaveChangesAsync(cancellationToken);
        await users.UpdateAsync(user);
        return Results.NoContent();
    }
}
