using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Api.Infrastructure.Persistence;
using TaskFlow.Api.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Infrastructure.Persistence;

[Collection(TaskFlowApiCollection.Name)]
public sealed class DependencyFailureIntegrationTests(
    TaskFlowTestEnvironment environment,
    ITestOutputHelper output)
    : IntegrationTestBase(environment, output, useRedis: true, authenticateAsAdmin: true)
{
    private const int BoardLockNamespace = 0x424F4152;
    private const int WorkspaceLockNamespace = 0x54534B46;

    [Fact]
    public async Task Live_application_reports_database_readiness_loss_and_recovers_without_restart()
    {
        var isolated = await IsolatedPostgresApplication.CreateAsync(Output);
        Exception? failure = null;
        try
        {
            using var healthClient = isolated.Factory.CreateIntegrationClient();
            using var boardClient = isolated.Factory.CreateIntegrationClient();
            boardClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                await isolated.Factory.CreateAdminTokenAsync());
            var workspaces = await boardClient.GetFromJsonAsync<JsonElement>("/api/workspaces");
            var workspaceId = Assert.Single(workspaces.EnumerateArray(),
                workspace => workspace.GetProperty("name").GetString() == "TaskFlow")
                .GetProperty("id").GetGuid();
            var boards = await boardClient.GetFromJsonAsync<JsonElement>($"/api/workspaces/{workspaceId}/boards");
            var boardId = Assert.Single(boards.EnumerateArray(),
                board => board.GetProperty("name").GetString() == "Deploy to production")
                .GetProperty("id").GetGuid();
            var boardPath = $"/api/boards/{boardId}";

            using (var ready = await GetWithTimeoutAsync(healthClient, "/health/ready"))
            {
                Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            }
            using (var board = await GetWithTimeoutAsync(boardClient, boardPath))
            {
                Assert.Equal(HttpStatusCode.OK, board.StatusCode);
            }

            using (var pauseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                await isolated.PauseAsync(pauseTimeout.Token);
            }

            using (var live = await GetWithTimeoutAsync(healthClient, "/health/live"))
            {
                Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            }
            using (var ready = await GetWithTimeoutAsync(healthClient, "/health/ready"))
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
            }

            using (var unpauseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                await isolated.UnpauseAsync(unpauseTimeout.Token);
            }

            await WaitForStatusAsync(
                healthClient,
                "/health/ready",
                HttpStatusCode.OK,
                TimeSpan.FromSeconds(15));
            using var recoveredBoard = await GetWithTimeoutAsync(boardClient, boardPath);
            Assert.Equal(HttpStatusCode.OK, recoveredBoard.StatusCode);
        }
        catch (Exception exception)
        {
            using var logTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Output.WriteLine(await isolated.GetPostgresLogsAsync(logTimeout.Token));
            failure = exception;
        }

        await isolated.DisposeAsync(failure);
    }

    [Fact]
    public async Task Canceled_request_waiting_for_board_lock_rolls_back_and_releases_workspace_lock()
    {
        var workspaceId = await CreateWorkspaceAsync("Canceled lock waiter");
        var boardId = await CreateBoardAsync(workspaceId, "Cancellation boundary");
        var before = await ReadBoardStateAsync(boardId);
        var title = $"Must not persist {Guid.NewGuid():N}";

        using var lockScope = Application.Services.CreateScope();
        var lockDb = lockScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var lockTransaction = await lockDb.Database.BeginTransactionAsync();
        await BoardInvariantLock.AcquireAsync(lockDb, boardId);

        using var requestCancellation = new CancellationTokenSource();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/boards/{boardId}/tasks")
        {
            Content = JsonContent.Create(new { title }),
        };
        var requestTask = Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            requestCancellation.Token);

        try
        {
            await WaitForAdvisoryLockCountAsync(
                BoardLockNamespace,
                boardId,
                granted: false,
                expectedCount: 1);

            requestCancellation.Cancel();
            using var canceledResponse = await WaitForCanceledRequestAsync(requestTask);

            await WaitForAdvisoryLockCountAsync(
                BoardLockNamespace,
                boardId,
                granted: false,
                expectedCount: 0);
            await WaitForAdvisoryLockCountAsync(
                WorkspaceLockNamespace,
                workspaceId,
                granted: true,
                expectedCount: 0);

            var afterCancellation = await ReadBoardStateAsync(boardId);
            Assert.Equal(before, afterCancellation);
            Assert.False(await TaskExistsAsync(boardId, title));
        }
        catch
        {
            using var diagnosticsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                var diagnostics = await Environment.CaptureContainerLogsAsync(
                    "advisory-lock-cancellation-failure",
                    diagnosticsTimeout.Token);
                Output.WriteLine($"TaskFlow Testcontainers diagnostics: {diagnostics}");
            }
            catch (Exception diagnosticsException)
            {
                Output.WriteLine($"Could not capture cancellation diagnostics: {diagnosticsException}");
            }

            throw;
        }
        finally
        {
            requestCancellation.Cancel();
            await lockTransaction.RollbackAsync();
            await DrainResponseAsync(requestTask);
        }

        using var recoveredWrite = await Client.PostAsJsonAsync(
            $"/api/boards/{boardId}/tasks",
            new { title = "Connection remains usable" });
        Assert.Equal(HttpStatusCode.Created, recoveredWrite.StatusCode);
        Assert.Equal(before.Revision + 1, (await ReadBoardStateAsync(boardId)).Revision);
    }

    private static async Task<HttpResponseMessage> GetWithTimeoutAsync(
        HttpClient client,
        string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(7));
        return await client.GetAsync(path, timeout.Token);
    }

    private static async Task<HttpResponseMessage?> WaitForCanceledRequestAsync(
        Task<HttpResponseMessage> requestTask)
    {
        try
        {
            var response = await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
            if (response.IsSuccessStatusCode)
            {
                var statusCode = response.StatusCode;
                response.Dispose();
                Assert.Fail($"The canceled request unexpectedly completed with {(int)statusCode}.");
            }

            return response;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task WaitForStatusAsync(
        HttpClient client,
        string path,
        HttpStatusCode expectedStatus,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        HttpStatusCode? lastStatus = null;
        Exception? lastException = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var response = await GetWithTimeoutAsync(client, path);
                lastStatus = response.StatusCode;
                if (response.StatusCode == expectedStatus)
                {
                    return;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
            {
                lastException = exception;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException(
            $"{path} did not return {(int)expectedStatus} within {timeout}. " +
            $"Last status: {lastStatus?.ToString() ?? "none"}.",
            lastException);
    }

    private async Task WaitForAdvisoryLockCountAsync(
        int lockNamespace,
        Guid resourceId,
        bool granted,
        int expectedCount)
    {
        var lockKey = EntityInvariantLock.GetKey(lockNamespace, resourceId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            while (true)
            {
                using var scope = Application.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var count = await db.Database.SqlQuery<int>($"""
                        SELECT COUNT(*)::int AS "Value"
                        FROM pg_locks
                        WHERE locktype = 'advisory'
                          AND granted = {granted}
                          AND classid::bigint = {(long)(uint)(lockKey >> 32)}
                          AND objid::bigint = {(long)(uint)lockKey}
                          AND objsubid = 1
                          AND database = (
                              SELECT oid
                              FROM pg_database
                              WHERE datname = current_database())
                        """)
                    .SingleAsync(timeout.Token);
                if (count == expectedCount)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
            }
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Expected {expectedCount} PostgreSQL advisory lock(s) " +
                $"for namespace {lockNamespace}, resource {resourceId}, granted={granted}.",
                exception);
        }
    }

    private async Task<Guid> CreateWorkspaceAsync(string name)
    {
        using var response = await Client.PostAsJsonAsync(
            "/api/workspaces",
            new { name = $"{name} {Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateBoardAsync(Guid workspaceId, string name)
    {
        using var response = await Client.PostAsJsonAsync(
            $"/api/workspaces/{workspaceId}/boards",
            new { name, description = "Dependency failure test", color = "#2563eb" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<(long Revision, int TaskCount)> ReadBoardStateAsync(Guid boardId)
    {
        using var scope = Application.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var state = await db.Boards
            .AsNoTracking()
            .Where(board => board.Id == boardId)
            .Select(board => new { board.Revision, TaskCount = board.Tasks.Count })
            .SingleAsync();
        return (state.Revision, state.TaskCount);
    }

    private async Task<bool> TaskExistsAsync(Guid boardId, string title)
    {
        using var scope = Application.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tasks.AnyAsync(task => task.BoardId == boardId && task.Title == title);
    }

    private static async Task DrainResponseAsync(Task<HttpResponseMessage> responseTask)
    {
        try
        {
            using var response = await responseTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Preserve the coordination failure that triggered cleanup.
        }
    }
}
