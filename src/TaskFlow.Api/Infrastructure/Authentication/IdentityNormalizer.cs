namespace TaskFlow.Api.Infrastructure.Authentication;

public static class IdentityNormalizer
{
    public static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
