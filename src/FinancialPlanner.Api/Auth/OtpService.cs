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

    public async Task<string?> IssueAsync(Guid userId, string purpose, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var issuedRecently = await database.EmailOtps.CountAsync(otp => otp.UserId == userId && otp.Purpose == purpose &&
            otp.ExpiresAt > now.AddHours(-1), cancellationToken);
        if (issuedRecently >= MaximumIssuesPerHour)
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
            ExpiresAt = now.AddMinutes(10)
        });
        await database.SaveChangesAsync(cancellationToken);
        return code;
    }

    public async Task<bool> ConsumeAsync(Guid userId, string purpose, string code, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var otp = await database.EmailOtps.OrderByDescending(item => item.ExpiresAt).FirstOrDefaultAsync(item =>
            item.UserId == userId && item.Purpose == purpose && item.ConsumedAt == null &&
            item.ExpiresAt > now && item.FailedAttempts < MaximumAttempts, cancellationToken);
        if (otp is null)
        {
            return false;
        }

        otp.LastAttemptAt = now;
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(otp.CodeHash), Convert.FromHexString(Hash(code))))
        {
            otp.FailedAttempts++;
            await database.SaveChangesAsync(cancellationToken);
            return false;
        }

        otp.ConsumedAt = now;
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
