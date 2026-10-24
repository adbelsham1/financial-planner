using Microsoft.AspNetCore.Identity;

namespace FinancialPlanner.Infrastructure.Identity;

public sealed class AppUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }
}
