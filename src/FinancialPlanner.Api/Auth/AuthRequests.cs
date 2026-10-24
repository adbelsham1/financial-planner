namespace FinancialPlanner.Api.Auth;

public sealed record RegisterOwnerRequest(
    string DisplayName,
    string Email,
    string Password,
    string WorkspaceName);

public sealed record VerifyOtpRequest(string Email, string Code);

public sealed record SignInRequest(string Email, string Password);

public sealed record ResetPasswordRequest(string Email);

public sealed record CompletePasswordResetRequest(string Email, string Code, string NewPassword);
