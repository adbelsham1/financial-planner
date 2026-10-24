using System.Security.Cryptography;
using System.Text;
using FinancialPlanner.Infrastructure;
using FinancialPlanner.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace FinancialPlanner.Api.Auth;

public sealed class SessionService(JwtTokenService tokens, PlannerDbContext database)
{
    public async Task IssueAsync(
        AppUser user,
        Guid tenantId,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        database.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(refreshToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        });
        await database.SaveChangesAsync(cancellationToken);

        context.Response.Cookies.Append("fp_access", tokens.CreateAccessToken(user, tenantId),
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Strict,
                Secure = context.Request.IsHttps,
                MaxAge = TimeSpan.FromMinutes(15)
            });
        context.Response.Cookies.Append("fp_refresh", refreshToken,
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Strict,
                Secure = context.Request.IsHttps,
                MaxAge = TimeSpan.FromDays(7)
            });
    }

    public async Task<bool> RefreshAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.Cookies.TryGetValue("fp_refresh", out var refreshToken))
        {
            return false;
        }

        var record = await database.RefreshTokens.SingleOrDefaultAsync(token =>
            token.TokenHash == Hash(refreshToken)
            && token.RevokedAt == null
            && token.ExpiresAt > DateTimeOffset.UtcNow, cancellationToken);
        if (record is null)
        {
            return false;
        }

        record.RevokedAt = DateTimeOffset.UtcNow;
        var user = await database.Users.SingleAsync(item => item.Id == record.UserId, cancellationToken);
        var membership = await database.TenantMemberships.SingleAsync(item => item.UserId == user.Id, cancellationToken);
        await IssueAsync(user, membership.TenantId, context, cancellationToken);
        return true;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
