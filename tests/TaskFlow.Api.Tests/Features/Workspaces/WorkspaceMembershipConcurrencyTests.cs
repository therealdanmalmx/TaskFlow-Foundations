using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Api.Infrastructure.Caching;
using TaskFlow.Api.Infrastructure.Persistence;
using TaskFlow.Api.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Features.Workspaces;

[Collection(TaskFlowApiCollection.Name)]
public sealed class WorkspaceMembershipConcurrencyTests(
    TaskFlowTestEnvironment environment,
    ITestOutputHelper output)
    : IntegrationTestBase(environment, output, useRedis: true)
{
    private const int WorkspaceLockNamespace = 0x54534B46;

    [Fact]
    public async Task Ordered_last_owner_removals_across_replicas_preserve_exactly_one_owner()
    {
        var firstOwner = await Application.CreateUserScenarioAsync(
            "first-owner",
            createPersonalWorkspace: true);
        var secondOwner = await Application.CreateUserScenarioAsync("second-owner");
        var workspaceId = firstOwner.WorkspaceId!.Value;
        var boardId = firstOwner.BoardId!.Value;
        await AddMemberAsync(firstOwner.Token, workspaceId, secondOwner, "Owner");

        using var replica = Application.WithWebHostBuilder(_ => { });
        using var replicaClient = await StartReplicaAsync(replica);

        var (firstRemoval, secondRemoval) = await RunQueuedBehindWorkspaceLockAsync(
            workspaceId,
            () => replicaClient.SendAuthenticatedAsync(
                firstOwner.Token,
                HttpMethod.Delete,
                $"/api/workspaces/{workspaceId}/members/{firstOwner.UserId}"),
            () => Client.SendAuthenticatedAsync(
                secondOwner.Token,
                HttpMethod.Delete,
                $"/api/workspaces/{workspaceId}/members/{secondOwner.UserId}"));
        using (firstRemoval)
        using (secondRemoval)
        {
            Assert.Equal(HttpStatusCode.NoContent, firstRemoval.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, secondRemoval.StatusCode);
        }

        using var membersResponse = await Client.SendAuthenticatedAsync(
            secondOwner.Token,
            HttpMethod.Get,
            $"/api/workspaces/{workspaceId}/members");
        membersResponse.EnsureSuccessStatusCode();
        var members = await membersResponse.Content.ReadFromJsonAsync<JsonElement>();
        var remainingOwner = Assert.Single(
            members.EnumerateArray(),
            member => member.GetProperty("role").GetString() == "Owner");
        Assert.Equal(secondOwner.UserId, remainingOwner.GetProperty("userId").GetGuid());
        Assert.DoesNotContain(
            members.EnumerateArray(),
            member => member.GetProperty("userId").GetGuid() == firstOwner.UserId);

        using var removedOwnerView = await replicaClient.SendAuthenticatedAsync(
            firstOwner.Token,
            HttpMethod.Get,
            $"/api/boards/{boardId}");
        Assert.Equal(HttpStatusCode.NotFound, removedOwnerView.StatusCode);
        using var remainingOwnerView = await Client.SendAuthenticatedAsync(
            secondOwner.Token,
            HttpMethod.Get,
            $"/api/boards/{boardId}");
        Assert.Equal(HttpStatusCode.OK, remainingOwnerView.StatusCode);
    }

    [Fact]
    public async Task Removal_ordered_before_task_creation_rejects_the_removed_assignee()
    {
        var owner = await Application.CreateUserScenarioAsync(
            "assignment-owner",
            createPersonalWorkspace: true);
        var assignee = await Application.CreateUserScenarioAsync("removed-assignee");
        var workspaceId = owner.WorkspaceId!.Value;
        var boardId = owner.BoardId!.Value;
        await AddMemberAsync(owner.Token, workspaceId, assignee, "Member");

        using var replica = Application.WithWebHostBuilder(_ => { });
        using var replicaClient = await StartReplicaAsync(replica);

        var (removal, create) = await RunQueuedBehindWorkspaceLockAsync(
            workspaceId,
            () => Client.SendAuthenticatedAsync(
                owner.Token,
                HttpMethod.Delete,
                $"/api/workspaces/{workspaceId}/members/{assignee.UserId}"),
            () => replicaClient.SendAuthenticatedAsync(
                owner.Token,
                HttpMethod.Post,
                $"/api/boards/{boardId}/tasks",
                new
                {
                    title = "Cannot retain a removed assignee",
                    description = "The membership boundary wins first.",
                    priority = "High",
                    assigneeId = assignee.UserId,
                }));
        using (removal)
        using (create)
        {
            Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
            Assert.Contains(
                "Assignee must be a member",
                await create.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        using var boardResponse = await Client.SendAuthenticatedAsync(
            owner.Token,
            HttpMethod.Get,
            $"/api/boards/{boardId}");
        boardResponse.EnsureSuccessStatusCode();
        var board = await boardResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(board.GetProperty("tasks").EnumerateArray());

        using var removedMemberView = await replicaClient.SendAuthenticatedAsync(
            assignee.Token,
            HttpMethod.Get,
            $"/api/boards/{boardId}");
        Assert.Equal(HttpStatusCode.NotFound, removedMemberView.StatusCode);
    }

    [Fact]
    public async Task Assignment_ordered_before_removal_is_unassigned_and_repairs_shared_cache_revision()
    {
        var owner = await Application.CreateUserScenarioAsync(
            "cache-owner",
            createPersonalWorkspace: true);
        var assignee = await Application.CreateUserScenarioAsync("cache-assignee");
        var workspaceId = owner.WorkspaceId!.Value;
        var boardId = owner.BoardId!.Value;
        await AddMemberAsync(owner.Token, workspaceId, assignee, "Member");
        var taskId = await CreateTaskAsync(owner.Token, boardId, "Initially unassigned");

        using var replica = Application.WithWebHostBuilder(_ => { });
        using var replicaClient = await StartReplicaAsync(replica);

        await AssertCacheSequenceAsync(Client, owner.Token, $"/api/boards/{boardId}", "MISS", "HIT");
        await AssertCacheSequenceAsync(Client, owner.Token, $"/api/boards/{boardId}/stats", "MISS", "HIT");
        var initialRevision = await ReadBoardRevisionAsync(boardId);

        var (assignment, removal) = await RunQueuedBehindWorkspaceLockAsync(
            workspaceId,
            () => Client.SendAuthenticatedAsync(
                owner.Token,
                HttpMethod.Put,
                $"/api/tasks/{taskId}",
                new
                {
                    title = "Assigned before removal",
                    description = "The later membership write must unassign this task.",
                    priority = "Urgent",
                    dueDate = (string?)null,
                    assigneeId = assignee.UserId,
                }),
            () => replicaClient.SendAuthenticatedAsync(
                owner.Token,
                HttpMethod.Delete,
                $"/api/workspaces/{workspaceId}/members/{assignee.UserId}"));
        using (assignment)
        using (removal)
        {
            Assert.Equal(HttpStatusCode.OK, assignment.StatusCode);
            Assert.Equal(
                assignee.UserId,
                (await assignment.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("assigneeId")
                .GetGuid());
            Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
        }

        var committedRevision = await ReadBoardRevisionAsync(boardId);
        Assert.Equal(initialRevision + 2, committedRevision);

        using (var boardResponse = await Client.SendAuthenticatedAsync(
                   owner.Token,
                   HttpMethod.Get,
                   $"/api/boards/{boardId}"))
        {
            boardResponse.EnsureSuccessStatusCode();
            Assert.Equal("MISS", CacheHeader(boardResponse));
            var board = await boardResponse.Content.ReadFromJsonAsync<JsonElement>();
            var task = Assert.Single(board.GetProperty("tasks").EnumerateArray());
            Assert.Equal("Assigned before removal", task.GetProperty("title").GetString());
            Assert.Equal(JsonValueKind.Null, task.GetProperty("assigneeId").ValueKind);
            Assert.Equal(JsonValueKind.Null, task.GetProperty("assigneeName").ValueKind);
        }

        await AssertCacheSequenceAsync(
            Client,
            owner.Token,
            $"/api/boards/{boardId}/stats",
            "MISS",
            "HIT");
        using (var boardHit = await Client.SendAuthenticatedAsync(
                   owner.Token,
                   HttpMethod.Get,
                   $"/api/boards/{boardId}"))
        {
            boardHit.EnsureSuccessStatusCode();
            Assert.Equal("HIT", CacheHeader(boardHit));
        }

        var cache = Application.Services.GetRequiredService<BoardCache>();
        var (boardCacheStatus, boardJson, _) = await cache.GetVersionedAsync(
            boardId,
            BoardCache.BoardKey(boardId));
        var (statsCacheStatus, statsJson, _) = await cache.GetVersionedAsync(
            boardId,
            BoardCache.StatsKey(boardId));
        Assert.Equal(CacheStatus.Hit, boardCacheStatus);
        Assert.Equal(CacheStatus.Hit, statsCacheStatus);
        using (var boardDocument = JsonDocument.Parse(boardJson!))
        using (var statsDocument = JsonDocument.Parse(statsJson!))
        {
            Assert.Equal(
                committedRevision,
                boardDocument.RootElement.GetProperty("boardRevision").GetInt64());
            Assert.Equal(
                committedRevision,
                statsDocument.RootElement.GetProperty("boardRevision").GetInt64());
            var cachedTask = Assert.Single(
                boardDocument.RootElement
                    .GetProperty("board")
                    .GetProperty("tasks")
                    .EnumerateArray());
            Assert.Equal(JsonValueKind.Null, cachedTask.GetProperty("assigneeId").ValueKind);
        }

        using var removedMemberView = await replicaClient.SendAuthenticatedAsync(
            assignee.Token,
            HttpMethod.Get,
            $"/api/boards/{boardId}");
        Assert.Equal(HttpStatusCode.NotFound, removedMemberView.StatusCode);
    }

    [Fact]
    public async Task Admin_demotion_ordered_before_board_update_revokes_the_queued_write()
    {
        var owner = await Application.CreateUserScenarioAsync(
            "demotion-owner",
            createPersonalWorkspace: true);
        var admin = await Application.CreateUserScenarioAsync("demoted-admin");
        var workspaceId = owner.WorkspaceId!.Value;
        var boardId = owner.BoardId!.Value;
        await AddMemberAsync(owner.Token, workspaceId, admin, "Admin");

        using var replica = Application.WithWebHostBuilder(_ => { });
        using var replicaClient = await StartReplicaAsync(replica);

        var (demotion, update) = await RunQueuedBehindWorkspaceLockAsync(
            workspaceId,
            () => Client.SendAuthenticatedAsync(
                owner.Token,
                HttpMethod.Put,
                $"/api/workspaces/{workspaceId}/members/{admin.UserId}/role",
                new { role = "Member" }),
            () => replicaClient.SendAuthenticatedAsync(
                admin.Token,
                HttpMethod.Put,
                $"/api/boards/{boardId}",
                new
                {
                    name = "Must not persist",
                    description = "Authorization changed while this request waited.",
                    color = "#dc2626",
                }));
        using (demotion)
        using (update)
        {
            Assert.Equal(HttpStatusCode.OK, demotion.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        }

        using var boardResponse = await Client.SendAuthenticatedAsync(
            admin.Token,
            HttpMethod.Get,
            $"/api/boards/{boardId}");
        boardResponse.EnsureSuccessStatusCode();
        var board = await boardResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("My tasks", board.GetProperty("name").GetString());
        Assert.Equal("Member", board.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Member_removal_ordered_before_task_write_hides_the_task_and_preserves_its_state()
    {
        var owner = await Application.CreateUserScenarioAsync(
            "task-owner",
            createPersonalWorkspace: true);
        var member = await Application.CreateUserScenarioAsync("removed-writer");
        var workspaceId = owner.WorkspaceId!.Value;
        var boardId = owner.BoardId!.Value;
        await AddMemberAsync(owner.Token, workspaceId, member, "Member");
        var taskId = await CreateTaskAsync(owner.Token, boardId, "State must remain Todo");

        using var replica = Application.WithWebHostBuilder(_ => { });
        using var replicaClient = await StartReplicaAsync(replica);

        var (removal, statusUpdate) = await RunQueuedBehindWorkspaceLockAsync(
            workspaceId,
            () => Client.SendAuthenticatedAsync(
                owner.Token,
                HttpMethod.Delete,
                $"/api/workspaces/{workspaceId}/members/{member.UserId}"),
            () => replicaClient.SendAuthenticatedAsync(
                member.Token,
                HttpMethod.Put,
                $"/api/tasks/{taskId}/status",
                new { status = "Doing" }));
        using (removal)
        using (statusUpdate)
        {
            Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, statusUpdate.StatusCode);
        }

        using var ownerView = await Client.SendAuthenticatedAsync(
            owner.Token,
            HttpMethod.Get,
            $"/api/boards/{boardId}");
        ownerView.EnsureSuccessStatusCode();
        var board = await ownerView.Content.ReadFromJsonAsync<JsonElement>();
        var task = Assert.Single(board.GetProperty("tasks").EnumerateArray());
        Assert.Equal("Todo", task.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, task.GetProperty("completedAt").ValueKind);

        using var removedMemberView = await replicaClient.SendAuthenticatedAsync(
            member.Token,
            HttpMethod.Get,
            $"/api/boards/{boardId}");
        Assert.Equal(HttpStatusCode.NotFound, removedMemberView.StatusCode);
    }

    private static async Task<HttpClient> StartReplicaAsync(WebApplicationFactory<Program> replica)
    {
        var client = replica.CreateIntegrationClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        try
        {
            using var live = await client.GetAsync("/health/live");
            live.EnsureSuccessStatusCode();
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task AddMemberAsync(
        string ownerToken,
        Guid workspaceId,
        TestUser member,
        string role)
    {
        using var response = await Client.SendAuthenticatedAsync(
            ownerToken,
            HttpMethod.Post,
            $"/api/workspaces/{workspaceId}/members",
            new { member.Username, role });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<Guid> CreateTaskAsync(string token, Guid boardId, string title)
    {
        using var response = await Client.SendAuthenticatedAsync(
            token,
            HttpMethod.Post,
            $"/api/boards/{boardId}/tasks",
            new { title });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<long> ReadBoardRevisionAsync(Guid boardId)
    {
        using var scope = Application.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Boards
            .AsNoTracking()
            .Where(board => board.Id == boardId)
            .Select(board => board.Revision)
            .SingleAsync();
    }

    private async Task<(HttpResponseMessage First, HttpResponseMessage Second)>
        RunQueuedBehindWorkspaceLockAsync(
            Guid workspaceId,
            Func<Task<HttpResponseMessage>> sendFirst,
            Func<Task<HttpResponseMessage>> sendSecond)
    {
        using var lockScope = Application.Services.CreateScope();
        var lockDb = lockScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var lockTransaction = await lockDb.Database.BeginTransactionAsync();
        await WorkspaceInvariantLock.AcquireAsync(lockDb, workspaceId);

        var firstTask = sendFirst();
        Task<HttpResponseMessage>? secondTask = null;
        try
        {
            await WaitForBlockedWorkspaceLockAsync(workspaceId, expectedCount: 1);
            secondTask = sendSecond();
            await WaitForBlockedWorkspaceLockAsync(workspaceId, expectedCount: 2);
        }
        catch
        {
            await lockTransaction.RollbackAsync();
            await DrainResponseAsync(firstTask);
            await DrainResponseAsync(secondTask);
            throw;
        }

        await lockTransaction.RollbackAsync();
        HttpResponseMessage? first = null;
        try
        {
            first = await firstTask.WaitAsync(TimeSpan.FromSeconds(30));
            var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(30));
            return (first, second);
        }
        catch
        {
            first?.Dispose();
            await DrainResponseAsync(firstTask);
            await DrainResponseAsync(secondTask);
            throw;
        }
    }

    private async Task WaitForBlockedWorkspaceLockAsync(Guid workspaceId, int expectedCount)
    {
        var lockKey = EntityInvariantLock.GetKey(WorkspaceLockNamespace, workspaceId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            while (true)
            {
                using var scope = Application.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var blocked = await db.Database.SqlQuery<int>($"""
                        SELECT COUNT(*)::int AS "Value"
                        FROM pg_locks
                        WHERE locktype = 'advisory'
                          AND NOT granted
                          AND classid::bigint = {(long)(uint)(lockKey >> 32)}
                          AND objid::bigint = {(long)(uint)lockKey}
                          AND objsubid = 1
                          AND database = (
                              SELECT oid
                              FROM pg_database
                              WHERE datname = current_database())
                        """)
                    .SingleAsync(timeout.Token);
                if (blocked >= expectedCount)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
            }
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Expected {expectedCount} blocked PostgreSQL workspace lock request(s) " +
                $"for workspace {workspaceId}.",
                exception);
        }
    }

    private static async Task AssertCacheSequenceAsync(
        HttpClient client,
        string token,
        string path,
        params string[] expected)
    {
        foreach (var cacheStatus in expected)
        {
            using var response = await client.SendAuthenticatedAsync(token, HttpMethod.Get, path);
            response.EnsureSuccessStatusCode();
            Assert.Equal(cacheStatus, CacheHeader(response));
        }
    }

    private static string CacheHeader(HttpResponseMessage response) =>
        response.Headers.GetValues("X-Cache").Single();

    private static async Task DrainResponseAsync(Task<HttpResponseMessage>? responseTask)
    {
        if (responseTask is null)
        {
            return;
        }

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
