namespace FinancialPlanner.Api.Email;

public interface IAuthenticationEmailSender
{
    Task SendOtpAsync(string recipient, string code, string purpose, CancellationToken cancellationToken);
}
