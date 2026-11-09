using System.Security.Cryptography;

namespace TaskFlow.Api.Infrastructure.Authentication;

public static class PasswordHasher
{
    private const int Iterations = 600_000;
    private const int MinimumAcceptedIterations = 100_000;
    private const int MaximumAcceptedIterations = 1_000_000;
    private const int MaximumPasswordLength = 200;
    private const int MaximumStoredHashLength = 200;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string Prefix = "pbkdf2-sha256";

    public static string Hash(string password)
    {
        if (password.Length is 0 or > MaximumPasswordLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(password),
                $"Password must contain between 1 and {MaximumPasswordLength} characters.");
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        if (password.Length > MaximumPasswordLength || stored.Length > MaximumStoredHashLength
            || stored.Split('$') is not [Prefix, var iterationsPart, var saltPart, var hashPart]
            || !int.TryParse(iterationsPart, out var iterations)
            || iterations is < MinimumAcceptedIterations or > MaximumAcceptedIterations)
        {
            return false;
        }

        Span<byte> salt = stackalloc byte[SaltSize];
        Span<byte> expected = stackalloc byte[HashSize];
        if (!Convert.TryFromBase64String(saltPart, salt, out var saltBytesWritten)
            || saltBytesWritten != SaltSize
            || !Convert.TryFromBase64String(hashPart, expected, out var hashBytesWritten)
            || hashBytesWritten != HashSize)
        {
            return false;
        }

        Span<byte> actual = stackalloc byte[HashSize];
        Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            actual,
            iterations,
            HashAlgorithmName.SHA256);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
