using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FinancialPlanner.Api.Email;
using FinancialPlanner.Infrastructure;
using FinancialPlanner.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FinancialPlanner.Api.Auth;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth");
        group.MapPost("/register", RegisterAsync);
        group.MapPost("/verify-email", VerifyEmailAsync);
        group.MapPost("/verify-email/resend", ResendEmailVerificationAsync);
        group.MapPost("/sign-in", BeginSignInAsync);
        group.MapPost("/sign-in/resend", ResendSignInAsync);
        group.MapPost("/sign-in/verify", CompleteSignInAsync);
        group.MapPost("/refresh", RefreshAsync);
        group.MapPost("/sign-out", SignOutAsync);
        group.MapPost("/password-reset", RequestPasswordResetAsync);
        group.MapPost("/password-reset/resend", RequestPasswordResetAsync);
        group.MapPost("/password-reset/verify", CompletePasswordResetAsync);
        group.MapGet("/me", GetCurrentUserAsync).RequireAuthorization("TenantRead");
        return group;
    }

    private static async Task<IResult> RegisterAsync(RegisterOwnerRequest request, UserManager<AppUser> users,
        PlannerDbContext database, OtpService otps, IAuthenticationEmailSender emailSender, AuditService audit,
        HttpContext context, CancellationToken cancellationToken)
    {
        if (!TryValidate(request, out var validation))
        {
            return Results.ValidationProblem(validation);
        }

        if (await users.FindByEmailAsync(request.Email.Trim()) is not null)
        {
            return Results.Conflict();
        }

        await using var transaction = database.Database.IsRelational()
            ? await database.Database.BeginTransactionAsync(cancellationToken) : null;
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim()
        };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return IdentityValidation(result);
        }

        var tenant = new Tenant { Name = request.WorkspaceName.Trim() };
        var legalEntity = new LegalEntity
        {
            TenantId = tenant.Id,
            Name = $"{user.DisplayName} Personal",
            Kind = LegalEntityKind.Personal
        };
        database.AddRange(tenant, legalEntity,
            new TenantMembership { TenantId = tenant.Id, UserId = user.Id, Role = TenantRole.Owner },
            new LegalEntityGrant { LegalEntityId = legalEntity.Id, UserId = user.Id, Role = TenantRole.Owner });
        var code = await otps.IssueAsync(user.Id, "email-confirmation", context, cancellationToken);
        audit.Add(context, "owner.registration.requested", $"tenant:{tenant.Id}", "success", tenant.Id, user.Id);
        await database.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        await emailSender.SendOtpAsync(user.Email, code!, "email-confirmation", cancellationToken);
        return Results.Accepted();
    }

    private static async Task<IResult> ResendEmailVerificationAsync(ResetPasswordRequest request,
        UserManager<AppUser> users, OtpService otps, IAuthenticationEmailSender emailSender, AuditService audit,
        HttpContext context, CancellationToken cancellationToken)
    {
        if (!TryValidate(request, out var validation)) return Results.ValidationProblem(validation);
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || user.EmailConfirmed)
        {
            return Results.Accepted();
        }
        return await IssueOtpAsync(user, "email-confirmation", users, otps, emailSender, audit, context, cancellationToken);
    }

    private static async Task<IResult> VerifyEmailAsync(VerifyOtpRequest request, UserManager<AppUser> users,
        OtpService otps, PlannerDbContext database, AuditService audit, HttpContext context, CancellationToken cancellationToken)
    {
        if (!TryValidate(request, out var validation)) return Results.ValidationProblem(validation);
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return Results.BadRequest();
        }
        await using var transaction = database.Database.IsRelational() ? await database.Database.BeginTransactionAsync(cancellationToken) : null;
        if (!await otps.ConsumeAsync(user.Id, "email-confirmation", request.Code, context, cancellationToken)) return Results.BadRequest();
        user.EmailConfirmed = true;
        await users.UpdateAsync(user);
        var tenantId = await database.TenantMemberships.Where(x => x.UserId == user.Id).Select(x => (Guid?)x.TenantId)
            .SingleOrDefaultAsync(cancellationToken);
        audit.Add(context, "owner.email-confirmed", $"user:{user.Id}", "success", tenantId, user.Id);
        await database.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> BeginSignInAsync(SignInRequest request, UserManager<AppUser> users,
        SignInManager<AppUser> signIn, OtpService otps, IAuthenticationEmailSender emailSender, AuditService audit,
        PlannerDbContext database, HttpContext context, CancellationToken cancellationToken)
    {
        if (!TryValidate(request, out var validation)) return Results.ValidationProblem(validation);
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.EmailConfirmed ||
            !(await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)).Succeeded)
        {
            audit.Add(context, "authentication.password", "email", "failure");
            await database.SaveChangesAsync(cancellationToken);
            return Results.Unauthorized();
        }
        return await IssueOtpAsync(user, "sign-in", users, otps, emailSender, audit, context, cancellationToken);
    }

    private static async Task<IResult> ResendSignInAsync(ResetPasswordRequest request, UserManager<AppUser> users,
        OtpService otps, IAuthenticationEmailSender emailSender, AuditService audit, HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!TryValidate(request, out var validation)) return Results.ValidationProblem(validation);
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.EmailConfirmed) return Results.Accepted();
        return await IssueOtpAsync(user, "sign-in", users, otps, emailSender, audit, context, cancellationToken);
    }

    private static async Task<IResult> CompleteSignInAsync(VerifyOtpRequest request, UserManager<AppUser> users,
        OtpService otps, PlannerDbContext database, SessionService sessions, AuditService audit, HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!TryValidate(request, out var validation)) return Results.ValidationProblem(validation);
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null) return Results.BadRequest();
        await using var transaction = database.Database.IsRelational() ? await database.Database.BeginTransactionAsync(cancellationToken) : null;
        if (!await otps.ConsumeAsync(user.Id, "sign-in", request.Code, context, cancellationToken)) return Results.BadRequest();
        var membership = await database.TenantMemberships.SingleAsync(x => x.UserId == user.Id, cancellationToken);
        audit.Add(context, "authentication.sign-in", $"user:{user.Id}", "success", membership.TenantId, user.Id);
        await database.SaveChangesAsync(cancellationToken);
        await sessions.IssueAsync(user, membership, context, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> RequestPasswordResetAsync(ResetPasswordRequest request, UserManager<AppUser> users,
        OtpService otps, IAuthenticationEmailSender emailSender, AuditService audit, HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!TryValidate(request, out var validation)) return Results.ValidationProblem(validation);
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.EmailConfirmed) return Results.Accepted();
        return await IssueOtpAsync(user, "password-reset", users, otps, emailSender, audit, context, cancellationToken);
    }

    private static async Task<IResult> CompletePasswordResetAsync(CompletePasswordResetRequest request, UserManager<AppUser> users,
        OtpService otps, PlannerDbContext database, AuditService audit, HttpContext context, CancellationToken cancellationToken)
    {
        if (!TryValidate(request, out var validation)) return Results.ValidationProblem(validation);
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null) return Results.BadRequest();
        await using var transaction = database.Database.IsRelational() ? await database.Database.BeginTransactionAsync(cancellationToken) : null;
        if (!await otps.ConsumeAsync(user.Id, "password-reset", request.Code, context, cancellationToken)) return Results.BadRequest();
        var reset = await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), request.NewPassword);
        if (!reset.Succeeded) return IdentityValidation(reset);
        var activeSessions = await database.RefreshTokens.Where(x => x.UserId == user.Id && x.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var session in activeSessions) session.RevokedAt = DateTimeOffset.UtcNow;
        var tenantId = await database.TenantMemberships.Where(x => x.UserId == user.Id).Select(x => (Guid?)x.TenantId)
            .SingleOrDefaultAsync(cancellationToken);
        audit.Add(context, "authentication.password-reset", $"user:{user.Id}", "success", tenantId, user.Id);
        await database.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> RefreshAsync(SessionService sessions, HttpContext context, CancellationToken cancellationToken) =>
        await sessions.RefreshAsync(context, cancellationToken) ? Results.NoContent() : Results.Unauthorized();

    private static async Task<IResult> SignOutAsync(SessionService sessions, HttpContext context, CancellationToken cancellationToken)
    {
        await sessions.RevokeAsync(context, cancellationToken);
        return Results.NoContent();
    }

    [Authorize(Policy = "TenantRead")]
    private static async Task<IResult> GetCurrentUserAsync(ClaimsPrincipal principal, PlannerDbContext database,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var membership = await database.TenantMemberships.SingleAsync(x => x.UserId == userId, cancellationToken);
        return Results.Ok(new { membership.TenantId, UserId = userId, Role = membership.Role.ToString() });
    }

    private static async Task<IResult> IssueOtpAsync(AppUser user, string purpose, UserManager<AppUser> users,
        OtpService otps, IAuthenticationEmailSender emailSender, AuditService audit, HttpContext context,
        CancellationToken cancellationToken)
    {
        var code = await otps.IssueAsync(user.Id, purpose, context, cancellationToken);
        if (code is null) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        audit.Add(context, $"authentication.{purpose}.issued", $"user:{user.Id}", "success");
        await audit.SaveChangesAsync(cancellationToken);
        await emailSender.SendOtpAsync(user.Email!, code, purpose, cancellationToken);
        return Results.Accepted();
    }

    private static IResult IdentityValidation(IdentityResult result) => Results.ValidationProblem(result.Errors
        .GroupBy(error => error.Code).ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));
    private static bool TryValidate(RegisterOwnerRequest request, out Dictionary<string, string[]> errors)
    {
        errors = [];
        AddIfInvalid(errors, "displayName", request.DisplayName, 128);
        AddIfInvalid(errors, "workspaceName", request.WorkspaceName, 128);
        AddIfInvalid(errors, "email", request.Email, 256, email: true);
        AddIfInvalid(errors, "password", request.Password, 1024);
        return errors.Count == 0;
    }
    private static bool TryValidate(VerifyOtpRequest request, out Dictionary<string, string[]> errors)
    {
        errors = []; AddIfInvalid(errors, "email", request.Email, 256, email: true);
        if (string.IsNullOrWhiteSpace(request.Code) || !System.Text.RegularExpressions.Regex.IsMatch(request.Code, "^\\d{6}$")) errors["code"] = ["A six digit code is required."];
        return errors.Count == 0;
    }
    private static bool TryValidate(SignInRequest request, out Dictionary<string, string[]> errors)
    {
        errors = []; AddIfInvalid(errors, "email", request.Email, 256, email: true); AddIfInvalid(errors, "password", request.Password, 1024); return errors.Count == 0;
    }
    private static bool TryValidate(ResetPasswordRequest request, out Dictionary<string, string[]> errors)
    {
        errors = []; AddIfInvalid(errors, "email", request.Email, 256, email: true); return errors.Count == 0;
    }
    private static bool TryValidate(CompletePasswordResetRequest request, out Dictionary<string, string[]> errors)
    {
        errors = []; AddIfInvalid(errors, "email", request.Email, 256, email: true);
        if (string.IsNullOrWhiteSpace(request.Code) || !System.Text.RegularExpressions.Regex.IsMatch(request.Code, "^\\d{6}$")) errors["code"] = ["A six digit code is required."];
        AddIfInvalid(errors, "newPassword", request.NewPassword, 1024); return errors.Count == 0;
    }
    private static void AddIfInvalid(Dictionary<string, string[]> errors, string name, string? value, int maxLength, bool email = false)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength || (email && !System.Net.Mail.MailAddress.TryCreate(value.Trim(), out _)))
            errors[name] = [$"{name} is required and must be valid."];
    }
}
