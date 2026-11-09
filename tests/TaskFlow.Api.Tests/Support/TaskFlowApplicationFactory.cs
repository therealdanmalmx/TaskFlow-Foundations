using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskFlow.Api.Domain.Boards;
using TaskFlow.Api.Domain.Users;
using TaskFlow.Api.Domain.Workspaces;
using TaskFlow.Api.Features.Shared;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Support;

public sealed class TaskFlowApplicationFactory(
    string databaseConnectionString,
    string? redisConnectionString,
    string webRoot,
    ITestOutputHelper? output = null) : WebApplicationFactory<Program>
{
    public const string AdminUsername = "admin";
    public const string AdminPassword = "test-admin-password";
    public const string AdminEmail = "admin@taskflow.local";
    public const string AdminDisplayName = "TaskFlow Admin";
    public const string JwtSecret = "test-jwt-secret-0123456789abcdef-0123456789abcdef";
    public const string ScenarioPassword = "scenario-password-123";

    private static readonly Lazy<string> ScenarioPasswordHash = new(
        () => PasswordHasher.Hash(ScenarioPassword),
        LazyThreadSafetyMode.ExecutionAndPublication);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseWebRoot(webRoot);
        builder.UseSetting("ConnectionStrings:Db", databaseConnectionString);
        builder.UseSetting("ConnectionStrings:Redis", redisConnectionString ?? string.Empty);
        builder.UseSetting("RUN_MIGRATIONS", "true");
        builder.UseSetting("Auth:JwtSecret", JwtSecret);
        builder.UseSetting("Auth:AdminUsername", AdminUsername);
        builder.UseSetting("Auth:AdminPassword", AdminPassword);
        builder.UseSetting("Auth:AdminEmail", AdminEmail);
        builder.UseSetting("Auth:AdminDisplayName", AdminDisplayName);

        if (output is not null)
        {
            builder.ConfigureLogging(logging => logging.AddProvider(new TestOutputLoggerProvider(output)));
        }
    }

    public async Task<string> CreateAdminTokenAsync(CancellationToken cancellationToken = default)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var normalizedUsername = IdentityNormalizer.Normalize(AdminUsername);
        var user = await db.Users
            .SingleAsync(candidate => candidate.NormalizedUsername == normalizedUsername, cancellationToken);
        return scope.ServiceProvider.GetRequiredService<TokenService>().CreateSession(user).Token;
    }

    public async Task<TestUser> CreateUserScenarioAsync(
        string prefix,
        bool createPersonalWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prefix) || prefix.Length > 39)
        {
            throw new ArgumentException(
                "The scenario prefix must contain between 1 and 39 characters.",
                nameof(prefix));
        }

        var unique = Guid.NewGuid().ToString("N")[..10];
        var username = $"{prefix}-{unique}";
        var displayName = $"{prefix} {unique}";
        var now = DateTimeOffset.UtcNow;

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var user = new User
        {
            Username = username,
            NormalizedUsername = IdentityNormalizer.Normalize(username),
            Email = $"{username}@example.test",
            NormalizedEmail = IdentityNormalizer.Normalize($"{username}@example.test"),
            DisplayName = displayName,
            PasswordHash = ScenarioPasswordHash.Value,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        Workspace? workspace = null;
        Board? board = null;
        if (createPersonalWorkspace)
        {
            workspace = new Workspace
            {
                Name = $"{displayName}'s Workspace",
                Slug = username,
                CreatedByUserId = user.Id,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Workspaces.Add(workspace);
            await db.SaveChangesAsync(cancellationToken);

            db.WorkspaceMemberships.Add(new WorkspaceMembership
            {
                WorkspaceId = workspace.Id,
                UserId = user.Id,
                Role = WorkspaceRole.Owner,
                JoinedAt = now,
            });
            board = new Board
            {
                WorkspaceId = workspace.Id,
                Name = "My tasks",
                Description = "A place to plan what comes next.",
                Color = InputValidation.DefaultBoardColor,
                CreatedByUserId = user.Id,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Boards.Add(board);
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        var token = scope.ServiceProvider.GetRequiredService<TokenService>().CreateSession(user).Token;
        return new TestUser(
            token,
            user.Id,
            user.Username,
            user.DisplayName,
            ScenarioPassword,
            workspace?.Id,
            board?.Id);
    }
}
