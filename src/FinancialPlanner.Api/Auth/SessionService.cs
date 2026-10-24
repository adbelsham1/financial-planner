using System.Security.Cryptography;
using System.Text;
using FinancialPlanner.Infrastructure;
using FinancialPlanner.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace FinancialPlanner.Api.Auth;

public sealed class SessionService(JwtTokenService tokens, PlannerDbContext database, AuditService audit)
{
    public async Task IssueAsync(
        AppUser user,
        TenantMembership membership,
        HttpContext context,
        CancellationToken cancellationToken,
        Guid? sessionId = null)
    {
        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        database.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(refreshToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            SessionId = sessionId ?? Guid.NewGuid()
        });
        await database.SaveChangesAsync(cancellationToken);

        context.Response.Cookies.Append("fp_access", tokens.CreateAccessToken(user, membership.TenantId, membership.Role),
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Strict,
                Secure = context.Request.IsHttps || !context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment(),
                MaxAge = TimeSpan.FromMinutes(15)
            });
        context.Response.Cookies.Append("fp_refresh", refreshToken,
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Strict,
                Secure = context.Request.IsHttps || !context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment(),
                MaxAge = TimeSpan.FromDays(7)
            });
    }

    public async Task<bool> RefreshAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.Cookies.TryGetValue("fp_refresh", out var refreshToken))
        {
            return false;
        }

        await using var transaction = database.Database.IsRelational()
            ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        var record = await database.RefreshTokens.SingleOrDefaultAsync(token => token.TokenHash == Hash(refreshToken), cancellationToken);
        if (record is null)
        {
            return false;
        }

        if (record.RevokedAt is not null || record.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await RevokeSessionAsync(record.SessionId, cancellationToken);
            audit.Add(context, "authentication.refresh", $"session:{record.SessionId}", "replay", actorId: record.UserId);
            await database.SaveChangesAsync(cancellationToken);
            DeleteCookies(context);
            return false;
        }

        record.RevokedAt = DateTimeOffset.UtcNow;
        var user = await database.Users.SingleAsync(item => item.Id == record.UserId, cancellationToken);
        var membership = await database.TenantMemberships.SingleAsync(item => item.UserId == user.Id, cancellationToken);
        var nextRawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var next = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(nextRawToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            SessionId = record.SessionId
        };
        // IssueAsync owns cookie creation and persistence; retaining a rotation family makes replay revocable.
        database.RefreshTokens.Add(next);
        record.ReplacedById = next.Id;
        audit.Add(context, "authentication.refresh", $"session:{record.SessionId}", "success", membership.TenantId, user.Id);
        await database.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        WriteCookies(user, membership, nextRawToken, context);
        return true;
    }

    public async Task RevokeAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (context.Request.Cookies.TryGetValue("fp_refresh", out var raw))
        {
            var token = await database.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == Hash(raw), cancellationToken);
            if (token is not null)
            {
                await RevokeSessionAsync(token.SessionId, cancellationToken);
                audit.Add(context, "authentication.sign-out", $"session:{token.SessionId}", "success", actorId: token.UserId);
                await database.SaveChangesAsync(cancellationToken);
            }
        }
        DeleteCookies(context);
    }

    private void WriteCookies(AppUser user, TenantMembership membership, string refreshToken, HttpContext context)
    {
        var secure = !context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment() || context.Request.IsHttps;
        context.Response.Cookies.Append("fp_access", tokens.CreateAccessToken(user, membership.TenantId, membership.Role),
            new CookieOptions { HttpOnly = true, IsEssential = true, SameSite = SameSiteMode.Strict, Secure = secure, MaxAge = TimeSpan.FromMinutes(15) });
        context.Response.Cookies.Append("fp_refresh", refreshToken,
            new CookieOptions { HttpOnly = true, IsEssential = true, SameSite = SameSiteMode.Strict, Secure = secure, MaxAge = TimeSpan.FromDays(7) });
    }

    private static void DeleteCookies(HttpContext context)
    {
        context.Response.Cookies.Delete("fp_access");
        context.Response.Cookies.Delete("fp_refresh");
    }

    private async Task RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var activeTokens = await database.RefreshTokens.Where(x => x.SessionId == sessionId && x.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in activeTokens) token.RevokedAt = DateTimeOffset.UtcNow;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
