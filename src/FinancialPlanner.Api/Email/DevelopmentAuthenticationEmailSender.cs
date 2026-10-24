namespace FinancialPlanner.Api.Email;

public sealed class DevelopmentAuthenticationEmailSender : IAuthenticationEmailSender
{
    private readonly List<DevelopmentEmail> _messages = [];
    private readonly Lock _lock = new();

    public Task SendOtpAsync(string recipient, string code, string purpose, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _messages.Add(new DevelopmentEmail(recipient, code, purpose, DateTimeOffset.UtcNow));
        }

        return Task.CompletedTask;
    }

    public DevelopmentEmail? GetLatest(string recipient)
    {
        lock (_lock)
        {
            return _messages.LastOrDefault(message =>
                string.Equals(message.Recipient, recipient, StringComparison.OrdinalIgnoreCase));
        }
    }
}

public sealed record DevelopmentEmail(string Recipient, string Code, string Purpose, DateTimeOffset SentAt);
