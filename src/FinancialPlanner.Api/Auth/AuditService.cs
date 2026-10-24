using System.Security.Cryptography;
using System.Text;
using FinancialPlanner.Infrastructure;
using FinancialPlanner.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace FinancialPlanner.Api.Auth;

public sealed class AuditService(PlannerDbContext database, IOptions<AuthOptions> options)
{
    public void Add(HttpContext context, string action, string target, string outcome, Guid? tenantId = null, Guid? actorId = null)
    {
        database.AuditEvents.Add(new AuditEvent
        {
            TenantId = tenantId,
            ActorId = actorId,
            Action = action,
            Target = target,
            Outcome = outcome,
            CorrelationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault() ?? context.TraceIdentifier,
            HashedIpAddress = HashIpAddress(context, options.Value.AuditIpHashKey)
        });
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => database.SaveChangesAsync(cancellationToken);

    private static string? HashIpAddress(HttpContext context, string key)
    {
        var address = context.Connection.RemoteIpAddress?.ToString();
        return string.IsNullOrWhiteSpace(address) ? null :
            Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(address)));
    }
}
