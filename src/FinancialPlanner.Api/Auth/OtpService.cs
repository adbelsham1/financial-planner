using System.Security.Cryptography;
using System.Text;
using FinancialPlanner.Infrastructure;
using FinancialPlanner.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace FinancialPlanner.Api.Auth;

public sealed class OtpService(PlannerDbContext database)
{
    private const int MaximumAttempts = 5;
    private const int MaximumIssuesPerHour = 5;
    private const int MaximumSourceIssuesPerHour = 20;

    public async Task<string?> IssueAsync(Guid userId, string purpose, HttpContext context, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var sourceHash = SourceHash(context);
        var issuedRecently = await database.EmailOtps.CountAsync(otp => otp.UserId == userId && otp.Purpose == purpose &&
            otp.ExpiresAt > now.AddHours(-1), cancellationToken);
        var sourceIssues = await database.EmailOtps.CountAsync(otp => otp.SourceHash == sourceHash && otp.Purpose == purpose &&
            otp.ExpiresAt > now.AddHours(-1), cancellationToken);
        if (issuedRecently >= MaximumIssuesPerHour || sourceIssues >= MaximumSourceIssuesPerHour)
        {
            return null;
        }

        var activeCodes = await database.EmailOtps.Where(otp => otp.UserId == userId && otp.Purpose == purpose &&
                otp.ConsumedAt == null && otp.ExpiresAt > now).ToListAsync(cancellationToken);
        foreach (var activeCode in activeCodes) activeCode.ConsumedAt = now;
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        database.EmailOtps.Add(new EmailOtp
        {
            UserId = userId,
            Purpose = purpose,
            CodeHash = Hash(code),
            SourceHash = sourceHash,
            ExpiresAt = now.AddMinutes(10)
        });
        await database.SaveChangesAsync(cancellationToken);
        return code;
    }

    public async Task<bool> ConsumeAsync(Guid userId, string purpose, string code, HttpContext context,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var otp = await database.EmailOtps.OrderByDescending(item => item.ExpiresAt).FirstOrDefaultAsync(item =>
            item.UserId == userId && item.Purpose == purpose && item.ConsumedAt == null &&
            item.ExpiresAt > now && item.FailedAttempts < MaximumAttempts, cancellationToken);
        if (otp is null)
        {
            return false;
        }

        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(otp.CodeHash), Convert.FromHexString(Hash(code))))
        {
            if (database.Database.IsRelational())
            {
                await database.EmailOtps.Where(item => item.Id == otp.Id && item.ConsumedAt == null)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.LastAttemptAt, now)
                        .SetProperty(item => item.FailedAttempts, item => item.FailedAttempts + 1), cancellationToken);
            }
            else
            {
                otp.LastAttemptAt = now;
                otp.FailedAttempts++;
                await database.SaveChangesAsync(cancellationToken);
            }
            return false;
        }

        if (database.Database.IsRelational())
        {
            // The conditional update is the claim; only one concurrent request can own this code.
            return await database.EmailOtps.Where(item => item.Id == otp.Id && item.ConsumedAt == null &&
                    item.ExpiresAt > now && item.FailedAttempts < MaximumAttempts)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ConsumedAt, now), cancellationToken) == 1;
        }

        otp.ConsumedAt = now;
        return true;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string SourceHash(HttpContext context) => Hash(context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
}
