using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace FinancialPlanner.Api.Email;

public sealed class GmailAuthenticationEmailSender(IOptions<GmailSmtpOptions> options) : IAuthenticationEmailSender
{
    private readonly GmailSmtpOptions _options = options.Value;

    public async Task SendOtpAsync(string recipient, string code, string purpose, CancellationToken cancellationToken)
    {
        var subject = purpose switch
        {
            "password-reset" => "Reset your Financial Planner password",
            "email-confirmation" => "Activate your Financial Planner workspace",
            _ => "Your Financial Planner sign-in code"
        };
        using var message = new MailMessage(_options.FromAddress, recipient, subject,
            $"Your verification code is {code}. It expires in 10 minutes. If you did not request this, ignore this email.");
        using var client = new SmtpClient("smtp.gmail.com", 587)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(_options.Username, _options.AppPassword)
        };
        await client.SendMailAsync(message, cancellationToken);
    }
}
