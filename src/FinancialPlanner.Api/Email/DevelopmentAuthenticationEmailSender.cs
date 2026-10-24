namespace FinancialPlanner.Api.Email;

public sealed class DevelopmentAuthenticationEmailSender(
    ILogger<DevelopmentAuthenticationEmailSender> logger) : IAuthenticationEmailSender
{
    public Task SendOtpAsync(string recipient, string code, CancellationToken cancellationToken)
    {
        logger.LogInformation("Authentication OTP issued for {Recipient}.", recipient);
        return Task.CompletedTask;
    }
}
