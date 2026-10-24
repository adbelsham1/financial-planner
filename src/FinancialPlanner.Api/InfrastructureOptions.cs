using System.ComponentModel.DataAnnotations;

namespace FinancialPlanner.Api;

public sealed class InfrastructureOptions
{
    public const string SectionName = "Infrastructure";

    [Required]
    [Url]
    public required string WebOrigin { get; init; }

    [Required]
    public required string DocumentsBucket { get; init; }

    [Required]
    [Url]
    public required string SpacesEndpoint { get; init; }

    [Required]
    public required string SpacesAccessKey { get; init; }

    [Required]
    public required string SpacesSecretKey { get; init; }
}
