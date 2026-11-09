using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Api.Infrastructure.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Support;

public sealed class TaskFlowTestEnvironment : IAsyncLifetime
{
    private const string TemplateDatabaseName = "taskflow_test_template";
    private static readonly string QuotedTemplateDatabaseName = QuoteIdentifier(TemplateDatabaseName);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase(TemplateDatabaseName)
        .Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();
    private readonly SemaphoreSlim _databaseGate = new(1, 1);
    private readonly string _runId = Guid.NewGuid().ToString("N");
    private readonly string _webRoot = Path.Combine(
        Path.GetTempPath(),
        $"taskflow-tests-{Guid.NewGuid():N}");
    private int _disposeState;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_webRoot);
        await File.WriteAllTextAsync(
            Path.Combine(_webRoot, "index.html"),
            "<!doctype html><html><body><div id=\"root\"></div></body></html>");

        try
        {
            using var startupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await Task.WhenAll(
                _postgres.StartAsync(startupTimeout.Token),
                _redis.StartAsync(startupTimeout.Token));

            var templateConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
            {
                Pooling = false,
            }.ConnectionString;
            using (var templateFactory = new TaskFlowApplicationFactory(
                       templateConnectionString,
                       redisConnectionString: null,
                       _webRoot))
            using (var client = templateFactory.CreateIntegrationClient())
            {
                var ready = await client.GetAsync("/health/ready");
                ready.EnsureSuccessStatusCode();
                await VerifyMigrationStateAsync(templateFactory);
            }

            await SealTemplateDatabaseAsync();
            await VerifyTemplateCloningAsync();
        }
        catch
        {
            var diagnostics = await CaptureContainerLogsAsync("environment-startup-failure");
            Console.Error.WriteLine($"TaskFlow Testcontainers diagnostics: {diagnostics}");
            try
            {
                await DisposeAsync();
            }
            catch (Exception cleanupException)
            {
                Console.Error.WriteLine($"TaskFlow Testcontainers cleanup failed: {cleanupException}");
            }

            throw;
        }
    }

    public async Task<TestApplication> CreateApplicationAsync(
        bool useRedis,
        bool startFromEmptyDatabase,
        ITestOutputHelper output,
        CancellationToken cancellationToken = default)
    {
        if (useRedis)
        {
            var flushed = await _redis.ExecAsync(["redis-cli", "FLUSHDB"], cancellationToken);
            if (flushed.ExitCode != 0)
            {
                throw new InvalidOperationException($"Redis FLUSHDB failed: {flushed.Stderr}");
            }
        }

        var databaseName = $"taskflow_test_{Guid.NewGuid():N}";
        await CreateDatabaseAsync(databaseName, startFromEmptyDatabase, cancellationToken);
        var connectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = databaseName,
        }.ConnectionString;
        var factory = new TaskFlowApplicationFactory(
            connectionString,
            useRedis ? _redis.GetConnectionString() : null,
            _webRoot,
            output);
        return new TestApplication(this, databaseName, factory);
    }

    public async Task<TestApplicationGroup> CreateApplicationsSharingEmptyDatabaseAsync(
        int count,
        ITestOutputHelper output,
        CancellationToken cancellationToken = default)
    {
        if (count < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "At least two applications are required.");
        }

        var databaseName = $"taskflow_test_{Guid.NewGuid():N}";
        await CreateDatabaseAsync(databaseName, startFromEmptyDatabase: true, cancellationToken);
        var connectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = databaseName,
        }.ConnectionString;
        var factories = Enumerable.Range(0, count)
            .Select(_ => new TaskFlowApplicationFactory(
                connectionString,
                redisConnectionString: null,
                _webRoot,
                output))
            .ToArray();
        return new TestApplicationGroup(this, databaseName, factories);
    }

    public async Task<string> CaptureContainerLogsAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(Path.GetTempPath(), "taskflow-testcontainers", _runId, reason);
        Directory.CreateDirectory(directory);
        await CaptureLogsAsync(_postgres, Path.Combine(directory, "postgres.log"), cancellationToken);
        await CaptureLogsAsync(_redis, Path.Combine(directory, "redis.log"), cancellationToken);
        return directory;
    }

    public async Task DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        List<Exception> failures = [];
        try
        {
            await _redis.DisposeAsync();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            await _postgres.DisposeAsync();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        _databaseGate.Dispose();
        try
        {
            Directory.Delete(_webRoot, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("Failed to dispose TaskFlow test infrastructure.", failures);
        }
    }

    internal async Task DropDatabaseAsync(
        string databaseName,
        CancellationToken cancellationToken = default)
    {
        ValidateDatabaseName(databaseName);
        await _databaseGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new NpgsqlConnection(GetAdminConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS {QuoteIdentifier(databaseName)} WITH (FORCE);";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            var diagnostics = await CaptureContainerLogsAsync(
                $"database-drop-failure-{databaseName}",
                CancellationToken.None);
            Console.Error.WriteLine($"TaskFlow Testcontainers diagnostics: {diagnostics}");
            throw;
        }
        finally
        {
            _databaseGate.Release();
        }
    }

    private async Task CreateDatabaseAsync(
        string databaseName,
        bool startFromEmptyDatabase,
        CancellationToken cancellationToken)
    {
        ValidateDatabaseName(databaseName);
        await _databaseGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new NpgsqlConnection(GetAdminConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = startFromEmptyDatabase
                ? $"CREATE DATABASE {QuoteIdentifier(databaseName)};"
                : $"CREATE DATABASE {QuoteIdentifier(databaseName)} WITH TEMPLATE {QuotedTemplateDatabaseName};";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            var diagnostics = await CaptureContainerLogsAsync(
                $"database-create-failure-{databaseName}",
                CancellationToken.None);
            Console.Error.WriteLine($"TaskFlow Testcontainers diagnostics: {diagnostics}");
            throw;
        }
        finally
        {
            _databaseGate.Release();
        }
    }

    private async Task SealTemplateDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(GetAdminConnectionString());
        await connection.OpenAsync();

        await using (var preventConnections = connection.CreateCommand())
        {
            preventConnections.CommandText =
                $"ALTER DATABASE {QuotedTemplateDatabaseName} WITH ALLOW_CONNECTIONS false IS_TEMPLATE true;";
            await preventConnections.ExecuteNonQueryAsync();
        }

        await using var terminateConnections = connection.CreateCommand();
        terminateConnections.CommandText =
            "SELECT pg_terminate_backend(pid) " +
            "FROM pg_stat_activity " +
            "WHERE datname = @database_name AND pid <> pg_backend_pid();";
        terminateConnections.Parameters.AddWithValue("database_name", TemplateDatabaseName);
        await terminateConnections.ExecuteNonQueryAsync();
    }

    private async Task VerifyTemplateCloningAsync()
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var databaseName = $"taskflow_test_clone_probe_{Guid.NewGuid():N}";
            await CreateDatabaseAsync(databaseName, startFromEmptyDatabase: false, CancellationToken.None);
            await DropDatabaseAsync(databaseName);
        }
    }

    private string GetAdminConnectionString() =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = "postgres",
            Pooling = false,
        }.ConnectionString;

    private static async Task VerifyMigrationStateAsync(TaskFlowApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var defined = db.Database.GetMigrations().ToArray();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();

        if (!defined.SequenceEqual(applied, StringComparer.Ordinal) || pending.Length != 0)
        {
            throw new InvalidOperationException(
                $"The template database is not fully migrated. " +
                $"Defined=[{string.Join(", ", defined)}], " +
                $"Applied=[{string.Join(", ", applied)}], " +
                $"Pending=[{string.Join(", ", pending)}].");
        }
    }

    private static async Task CaptureLogsAsync(
        DotNet.Testcontainers.Containers.IContainer container,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var (stdout, stderr) = await container.GetLogsAsync(ct: cancellationToken);
            await File.WriteAllTextAsync(
                path,
                $"STDOUT{Environment.NewLine}{stdout}{Environment.NewLine}" +
                $"STDERR{Environment.NewLine}{stderr}",
                cancellationToken);
        }
        catch (Exception exception)
        {
            await File.WriteAllTextAsync(
                path,
                $"Could not read container logs: {exception}",
                CancellationToken.None);
        }
    }

    private static void ValidateDatabaseName(string databaseName)
    {
        if (!databaseName.StartsWith("taskflow_test_", StringComparison.Ordinal) ||
            databaseName.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new ArgumentException("The test database name is invalid.", nameof(databaseName));
        }
    }

    private static string QuoteIdentifier(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
