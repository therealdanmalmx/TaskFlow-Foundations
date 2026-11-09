namespace TaskFlow.Api.Infrastructure.Authentication;

public static class AuthRateLimitPolicies
{
    public const string Login = "login";
    public const string Registration = "registration";
    public const string PasswordChange = "password-change";
}
