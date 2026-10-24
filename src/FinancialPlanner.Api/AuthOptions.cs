using System.ComponentModel.DataAnnotations;

namespace FinancialPlanner.Api;

public sealed class AuthOptions
{
    public const string SectionName = "Authentication";

    [Required]
    [MinLength(32)]
    public required string JwtSigningKey { get; init; }
}
