using TaskFlow.Api.Infrastructure.Authentication;
using Xunit;

namespace TaskFlow.Api.Tests.Infrastructure.Authentication;

public class PasswordHasherTests
{
    [Fact]
    public void Valid_hashes_round_trip_and_malformed_work_factors_are_rejected()
    {
        var password = "a-valid-password-123";
        var stored = PasswordHasher.Hash(password);
        var salt = Convert.ToBase64String(new byte[16]);
        var hash = Convert.ToBase64String(new byte[32]);

        Assert.True(PasswordHasher.Verify(password, stored));
        Assert.False(PasswordHasher.Verify("not-the-password", stored));
        Assert.False(PasswordHasher.Verify(
            password,
            $"pbkdf2-sha256$2147483647${salt}${hash}"));
        Assert.False(PasswordHasher.Verify(new string('x', 201), stored));
        Assert.Throws<ArgumentOutOfRangeException>(() => PasswordHasher.Hash(new string('x', 201)));
    }
}
