using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Api.Infrastructure.Persistence;
using TaskFlow.Api.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Infrastructure.Persistence;

[Collection(TaskFlowApiCollection.Name)]
public class MigrationApiTests(TaskFlowTestEnvironment environment, ITestOutputHelper output)
    : IntegrationTestBase(environment, output, startFromEmptyDatabase: true)
{
    private const int WorkspaceSlugLockNamespace = 0x534C5547;
    private const int WorkspaceSlugLockKey = 1;

    [Fact]
    public async Task A_fresh_database_is_migrated_and_seeded_by_application_startup()
    {
        using var scope = Application.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var defined = db.Database.GetMigrations().ToArray();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();

        Assert.Equal(["20260829184834_InitialCreate"], defined);
        Assert.Equal(defined, applied);
        Assert.Empty(pending);
        Assert.Equal(TaskFlowApplicationFactory.AdminUsername, (await db.Users.SingleAsync()).Username);
        Assert.Equal("Deploy to production", (await db.Boards.SingleAsync()).Name);
        Assert.Equal(10, await db.Tasks.CountAsync());

        var identifiers = (await db.Users.Select(user => user.Id).ToArrayAsync())
            .Concat(await db.Workspaces.Select(workspace => workspace.Id).ToArrayAsync())
            .Concat(await db.Boards.Select(board => board.Id).ToArrayAsync())
            .Concat(await db.Tasks.Select(task => task.Id).ToArrayAsync())
            .ToArray();
        Assert.Equal(identifiers.Length, identifiers.Distinct().Count());
        Assert.All(identifiers, id => Assert.Equal('4', id.ToString("D")[14]));
    }

    [Fact]
    public void InitialCreate_target_model_matches_the_current_model_and_snapshot()
    {
        using var scope = Application.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var migrations = db.GetService<IMigrationsAssembly>();
        var migrationType = Assert.Single(migrations.Migrations);
        Assert.Equal("20260829184834_InitialCreate", migrationType.Key);
        var migration = migrations.CreateMigration(migrationType.Value, db.Database.ProviderName!);

        var targetModel = migration.TargetModel;
        if (targetModel is IMutableModel mutableModel)
        {
            targetModel = mutableModel.FinalizeModel();
        }

        targetModel = db.GetService<IModelRuntimeInitializer>().Initialize(targetModel);
        var currentModel = db.GetService<IDesignTimeModel>().Model;
        var differences = db.GetService<IMigrationsModelDiffer>().GetDifferences(
            targetModel.GetRelationalModel(),
            currentModel.GetRelationalModel());

        Assert.Empty(differences);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Two_replicas_can_migrate_and_seed_the_same_fresh_database_concurrently()
    {
        await using var applications =
            await Environment.CreateApplicationsSharingEmptyDatabaseAsync(2, Output);

        var startupTasks = applications.Factories.Select(factory => Task.Run(async () =>
        {
            using var client = factory.CreateIntegrationClient();
            var response = await client.GetAsync("/health/ready");
            response.EnsureSuccessStatusCode();
        }));
        await Task.WhenAll(startupTasks);

        using var scope = applications.Factories[0].Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await db.Users.ToListAsync());
        Assert.Single(await db.Workspaces.ToListAsync());
        Assert.Single(await db.Boards.ToListAsync());
        Assert.Equal(10, await db.Tasks.CountAsync());
    }

    [Fact]
    public async Task Application_restart_is_migration_and_seed_idempotent()
    {
        using var restartedFactory = Application.WithWebHostBuilder(_ => { });
        using var restartedClient = restartedFactory.CreateIntegrationClient();
        using var ready = await restartedClient.GetAsync("/health/ready");
        ready.EnsureSuccessStatusCode();

        using var scope = restartedFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await db.Users.ToListAsync());
        Assert.Single(await db.Workspaces.ToListAsync());
        Assert.Single(await db.Boards.ToListAsync());
        Assert.Equal(10, await db.Tasks.CountAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Rolling_seed_and_live_workspace_allocation_share_the_slug_lock()
    {
        var token = await Application.CreateAdminTokenAsync();
        using var rollingFactory = Application.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Auth:AdminUsername", "rolling-admin");
            builder.UseSetting("Auth:AdminEmail", "rolling-admin@example.test");
            builder.UseSetting("Auth:AdminDisplayName", "Rolling Admin");
            builder.UseSetting("Auth:AdminPassword", "rolling-admin-password");
        });

        using var lockScope = Application.Services.CreateScope();
        var lockDb = lockScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var lockTransaction = await lockDb.Database.BeginTransactionAsync();
        await WorkspaceSlugAllocationLock.AcquireAsync(lockDb);

        var rollingStartup = Task.Run(async () =>
        {
            using var client = rollingFactory.CreateIntegrationClient();
            using var response = await client.GetAsync("/health/ready");
            response.EnsureSuccessStatusCode();
        });
        var liveWorkspaceWrite = Client.SendAuthenticatedAsync(
            token,
            HttpMethod.Post,
            "/api/workspaces",
            new { name = "TaskFlow" });

        var bothWaitedForTheSlugLock = false;
        try
        {
            bothWaitedForTheSlugLock = await WaitForSlugLockWaitersAsync(lockDb, expectedCount: 2);
        }
        finally
        {
            try
            {
                await lockTransaction.CommitAsync();
            }
            finally
            {
                if (!bothWaitedForTheSlugLock)
                {
                    await DrainTaskAsync(rollingStartup);
                    await DrainResponseAsync(liveWorkspaceWrite);
                }
            }
        }

        Assert.True(
            bothWaitedForTheSlugLock,
            "Both rolling seed and live workspace allocation should wait on the shared slug lock.");

        await rollingStartup;
        using var workspaceResponse = await liveWorkspaceWrite;
        Assert.Equal(HttpStatusCode.Created, workspaceResponse.StatusCode);

        using var verificationScope = Application.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var slugs = await verificationDb.Workspaces
            .OrderBy(workspace => workspace.Id)
            .Select(workspace => workspace.Slug)
            .ToArrayAsync();
        Assert.Equal(3, slugs.Length);
        Assert.Equal(3, slugs.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("taskflow", slugs);
        Assert.Contains("taskflow-2", slugs);
        Assert.Contains("taskflow-3", slugs);
    }

    private static async Task<bool> WaitForSlugLockWaitersAsync(
        AppDbContext db,
        int expectedCount)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        do
        {
            var waiting = await db.Database.SqlQuery<int>($"""
                    SELECT COUNT(*)::int AS "Value"
                    FROM pg_locks
                    WHERE locktype = 'advisory'
                      AND NOT granted
                      AND classid::bigint = {WorkspaceSlugLockNamespace}
                      AND objid::bigint = {WorkspaceSlugLockKey}
                    """)
                .SingleAsync();
            if (waiting >= expectedCount)
            {
                return true;
            }

            await Task.Delay(25);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return false;
    }

    private static async Task DrainTaskAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch
        {
            // Preserve the coordination failure that triggered cleanup.
        }
    }

    private static async Task DrainResponseAsync(Task<HttpResponseMessage> responseTask)
    {
        try
        {
            using var response = await responseTask.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch
        {
            // Preserve the coordination failure that triggered cleanup.
        }
    }
}
