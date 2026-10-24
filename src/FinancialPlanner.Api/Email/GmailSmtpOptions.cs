using System.ComponentModel.DataAnnotations;

namespace FinancialPlanner.Api.Email;

public sealed class GmailSmtpOptions
{
    public const string SectionName = "Email:Gmail";

    [Required, EmailAddress]
    public required string FromAddress { get; init; }

    [Required, EmailAddress]
    public required string Username { get; init; }

    [Required, MinLength(16)]
    public required string AppPassword { get; init; }
}
