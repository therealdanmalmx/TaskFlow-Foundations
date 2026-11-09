using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TaskFlow.Api.Infrastructure.Persistence;
using TaskFlow.Api.Infrastructure.Seeding;
using Xunit;

namespace TaskFlow.Api.Tests.Infrastructure.Seeding;

public class SeedConfigurationTests
{
    [Theory]
    [InlineData("Auth:AdminUsername", "x")]
    [InlineData("Auth:AdminEmail", "invalid@")]
    [InlineData("Auth:AdminDisplayName", "")]
    public async Task Invalid_admin_identity_configuration_fails_before_database_access(
        string key,
        string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=not-used;Database=not-used;Username=not-used;Password=not-used")
            .Options;
        await using var db = new AppDbContext(options);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SeedData.ApplyAsync(db, configuration, NullLogger.Instance));

        Assert.Contains(key, exception.Message, StringComparison.Ordinal);
    }
}
