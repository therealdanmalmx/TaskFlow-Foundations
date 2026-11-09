using Npgsql;
using System.Runtime.ExceptionServices;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Support;

public sealed class IsolatedPostgresApplication : IAsyncDisposable
{
    private readonly PostgreSqlContainer _postgres;
    private readonly ITestOutputHelper _output;
    private readonly string _webRoot;
    private bool _paused;
    private int _disposeState;

    private IsolatedPostgresApplication(
        PostgreSqlContainer postgres,
        string webRoot,
        TaskFlowApplicationFactory factory,
        ITestOutputHelper output)
    {
        _postgres = postgres;
        _webRoot = webRoot;
        _output = output;
        Factory = factory;
    }

    public TaskFlowApplicationFactory Factory { get; }

    public static async Task<IsolatedPostgresApplication> CreateAsync(
        ITestOutputHelper output,
        CancellationToken cancellationToken = default)
    {
        var webRoot = Path.Combine(
            Path.GetTempPath(),
            $"taskflow-isolated-postgres-{Guid.NewGuid():N}");
        var postgres = new PostgreSqlBuilder("postgres:18")
            .WithDatabase("taskflow")
            .Build();

        try
        {
            Directory.CreateDirectory(webRoot);
            await File.WriteAllTextAsync(
                Path.Combine(webRoot, "index.html"),
                "<!doctype html><html><body><div id=\"root\"></div></body></html>",
                cancellationToken);

            using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startupTimeout.CancelAfter(TimeSpan.FromMinutes(2));
            await postgres.StartAsync(startupTimeout.Token);

            var connectionString = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
            {
                Timeout = 1,
                CommandTimeout = 2,
                CancellationTimeout = 1_000,
                KeepAlive = 1,
            }.ConnectionString;
            var factory = new TaskFlowApplicationFactory(
                connectionString,
                redisConnectionString: null,
                webRoot,
                output);
            return new IsolatedPostgresApplication(postgres, webRoot, factory, output);
        }
        catch (Exception startupException)
        {
            await WriteContainerLogsAsync(postgres, output, "Isolated PostgreSQL startup failure");
            List<Exception> failures = [startupException];
            await TryCleanupAsync(
                failures,
                "disposing the PostgreSQL container after startup failure",
                () => postgres.DisposeAsync().AsTask(),
                TimeSpan.FromSeconds(20));
            try
            {
                TryDeleteDirectory(webRoot);
            }
            catch (Exception cleanupException)
            {
                failures.Add(cleanupException);
            }

            if (failures.Count > 1)
            {
                throw new AggregateException(
                    "The isolated PostgreSQL application failed to start and clean up.",
                    failures);
            }

            ExceptionDispatchInfo.Capture(startupException).Throw();
            throw;
        }
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposeState != 0, this);
        if (_paused)
        {
            return;
        }

        await _postgres.PauseAsync(cancellationToken);
        _paused = true;
    }

    public async Task UnpauseAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposeState != 0, this);
        if (!_paused)
        {
            return;
        }

        await _postgres.UnpauseAsync(cancellationToken);
        _paused = false;
    }

    public async Task<string> GetPostgresLogsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var (stdout, stderr) = await _postgres.GetLogsAsync(ct: cancellationToken);
            return $"STDOUT{Environment.NewLine}{stdout}{Environment.NewLine}" +
                $"STDERR{Environment.NewLine}{stderr}";
        }
        catch (Exception exception)
        {
            return $"Could not read isolated PostgreSQL logs: {exception}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        var failures = await CleanupAsync();
        if (failures.Count > 0)
        {
            throw new AggregateException(
                "Failed to dispose the isolated PostgreSQL test application.",
                failures);
        }
    }

    public async Task DisposeAsync(Exception? primaryFailure)
    {
        var cleanupFailures = await CleanupAsync();
        if (primaryFailure is not null)
        {
            if (cleanupFailures.Count > 0)
            {
                throw new AggregateException(
                    "The isolated PostgreSQL test failed and cleanup also failed.",
                    [primaryFailure, .. cleanupFailures]);
            }

            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        if (cleanupFailures.Count > 0)
        {
            throw new AggregateException(
                "Failed to dispose the isolated PostgreSQL test application.",
                cleanupFailures);
        }
    }

    private async Task<List<Exception>> CleanupAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return [];
        }

        List<Exception> failures = [];
        if (_paused)
        {
            await TryCleanupAsync(
                failures,
                "unpausing the PostgreSQL container",
                async () =>
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await _postgres.UnpauseAsync(timeout.Token);
                    _paused = false;
                },
                TimeSpan.FromSeconds(12));
        }

        await TryCleanupAsync(
            failures,
            "disposing the isolated application host",
            () => Factory.DisposeAsync().AsTask(),
            TimeSpan.FromSeconds(15));

        if (failures.Count > 0)
        {
            await WriteContainerLogsAsync(
                _postgres,
                _output,
                "Isolated PostgreSQL cleanup failure");
        }

        await TryCleanupAsync(
            failures,
            "disposing the isolated PostgreSQL container",
            () => _postgres.DisposeAsync().AsTask(),
            TimeSpan.FromSeconds(20));

        try
        {
            TryDeleteDirectory(_webRoot);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        return failures;
    }

    private static async Task TryCleanupAsync(
        ICollection<Exception> failures,
        string operation,
        Func<Task> cleanup,
        TimeSpan timeout)
    {
        try
        {
            await cleanup().WaitAsync(timeout);
        }
        catch (Exception exception)
        {
            failures.Add(new InvalidOperationException($"Failed while {operation}.", exception));
        }
    }

    private static async Task WriteContainerLogsAsync(
        PostgreSqlContainer postgres,
        ITestOutputHelper output,
        string reason)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var (stdout, stderr) = await postgres.GetLogsAsync(ct: timeout.Token);
            output.WriteLine(
                $"{reason}:{Environment.NewLine}" +
                $"STDOUT{Environment.NewLine}{stdout}{Environment.NewLine}" +
                $"STDERR{Environment.NewLine}{stderr}");
        }
        catch (Exception exception)
        {
            try
            {
                output.WriteLine($"{reason}: could not read container logs: {exception}");
            }
            catch
            {
                // Diagnostics must not replace the failure they are describing.
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
