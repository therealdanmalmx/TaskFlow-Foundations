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

namespace TaskFlow.Api.Tests.Features.Boards;

[Collection(TaskFlowApiCollection.Name)]
public class BoardIntegrationTests(
    TaskFlowTestEnvironment environment,
    ITestOutputHelper output)
    : IntegrationTestBase(environment, output, authenticateAsAdmin: true)
{
    private const int BoardLockNamespace = 0x424F4152;
    private const int WorkspaceLockNamespace = 0x54534B46;

    private string AdminToken => Client.DefaultRequestHeaders.Authorization!.Parameter!;

    [Fact]
    public async Task The_seeded_board_is_discoverable_with_its_deployment_checklist()
    {
        var workspaces = await Client.GetFromJsonAsync<JsonElement>("/api/workspaces");
        var workspaceId = Assert.Single(workspaces.EnumerateArray(),
            workspace => workspace.GetProperty("name").GetString() == "TaskFlow")
            .GetProperty("id").GetGuid();
        var boards = await Client.GetFromJsonAsync<JsonElement>($"/api/workspaces/{workspaceId}/boards");
        var summary = Assert.Single(boards.EnumerateArray(),
            board => board.GetProperty("name").GetString() == "Deploy to production");

        var board = await GetBoardAsync(summary.GetProperty("id").GetGuid());

        Assert.Equal("Deploy to production", board.GetProperty("name").GetString());
        string[] seededTitles =
        [
            "Provision a VPS", "Harden SSH access", "Configure the firewall",
            "Set up private networking", "Install Dokploy", "Point DNS at the server",
            "Deploy this app", "Set up automatic deployments",
            "Schedule database backups", "Run a restore drill",
        ];
        var tasks = board.GetProperty("tasks").EnumerateArray().ToArray();
        Assert.Equal(seededTitles.Order(), tasks
            .Select(task => task.GetProperty("title").GetString()).Order());
        Assert.Equal(5, tasks.Count(task => task.GetProperty("status").GetString() == "Done"));
    }

    [Fact]
    public async Task Board_CRUD_validation_and_listing_order_are_persisted()
    {
        var workspaceId = await CreateWorkspaceAsync("Board lifecycle");
        object[] invalidBoards =
        [
            new { name = "   ", description = "", color = "#2563eb" },
            new { name = new string('n', 101), description = "", color = "#2563eb" },
            new { name = "Too much detail", description = new string('d', 2_001), color = "#2563eb" },
            new { name = "Bad color", description = "", color = "blue" },
        ];

        foreach (var invalidBoard in invalidBoards)
        {
            using var invalid = await SendAsync(
                AdminToken,
                HttpMethod.Post,
                $"/api/workspaces/{workspaceId}/boards",
                invalidBoard);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        var first = await CreateBoardAsync(workspaceId, "  First board  ", "  First description  ", "#A1B2C3");
        var second = await CreateBoardAsync(workspaceId, "Second board");
        var third = await CreateBoardAsync(workspaceId, "Third board");

        Assert.Equal("First board", first.GetProperty("name").GetString());
        Assert.Equal("First description", first.GetProperty("description").GetString());
        Assert.Equal("#a1b2c3", first.GetProperty("color").GetString());

        var secondId = second.GetProperty("id").GetGuid();
        using (var invalidUpdate = await SendAsync(
                   AdminToken,
                   HttpMethod.Put,
                   $"/api/boards/{secondId}",
                   new { name = "Must not persist", description = "", color = "#12345" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalidUpdate.StatusCode);
        }

        using (var update = await SendAsync(
                   AdminToken,
                   HttpMethod.Put,
                   $"/api/boards/{secondId}",
                   new { name = "  Release train  ", description = "  Ship safely  ", color = "#ABCDEF" }))
        {
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
            var updated = await update.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Release train", updated.GetProperty("name").GetString());
            Assert.Equal("Ship safely", updated.GetProperty("description").GetString());
            Assert.Equal("#abcdef", updated.GetProperty("color").GetString());
            Assert.Equal("Owner", updated.GetProperty("role").GetString());
        }

        var firstId = first.GetProperty("id").GetGuid();
        using (var archive = await SendAsync(
                   AdminToken,
                   HttpMethod.Put,
                   $"/api/boards/{firstId}/archive",
                   new { isArchived = true }))
        {
            Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
            Assert.True((await archive.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("isArchived").GetBoolean());
        }

        using var listed = await SendAsync(
            AdminToken,
            HttpMethod.Get,
            $"/api/workspaces/{workspaceId}/boards");
        listed.EnsureSuccessStatusCode();
        var boards = (await listed.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToArray();
        Assert.Equal(
            [secondId, third.GetProperty("id").GetGuid(), firstId],
            boards.Select(board => board.GetProperty("id").GetGuid()));
        Assert.False(boards[0].GetProperty("isArchived").GetBoolean());
        Assert.False(boards[1].GetProperty("isArchived").GetBoolean());
        Assert.True(boards[2].GetProperty("isArchived").GetBoolean());

        var persisted = await GetBoardAsync(secondId);
        Assert.Equal("Release train", persisted.GetProperty("name").GetString());
        Assert.Equal(0, persisted.GetProperty("taskCount").GetInt32());
    }

    [Fact]
    public async Task Tenant_boundaries_hide_every_board_surface_and_membership_uniqueness_survives_a_race()
    {
        var workspaceId = await CreateWorkspaceAsync("Tenant boundary");
        var board = await CreateBoardAsync(workspaceId, "Private board");
        var boardId = board.GetProperty("id").GetGuid();
        var outsider = await Application.CreateUserScenarioAsync("outsider");

        var hiddenRequests = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Get, $"/api/workspaces/{workspaceId}/boards", null),
            (HttpMethod.Get, $"/api/boards/{boardId}", null),
            (HttpMethod.Get, $"/api/boards/{boardId}/stats", null),
            (HttpMethod.Get, $"/api/boards/{boardId}/activity", null),
            (HttpMethod.Put, $"/api/boards/{boardId}", new { name = "Guessed", description = "", color = "#2563eb" }),
            (HttpMethod.Put, $"/api/boards/{boardId}/archive", new { isArchived = true }),
            (HttpMethod.Post, $"/api/boards/{boardId}/tasks", new { title = "Guessed task" }),
        };

        foreach (var (method, path, body) in hiddenRequests)
        {
            using var hidden = await SendAsync(outsider.Token, method, path, body);
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        }

        var duplicateAdds = await Task.WhenAll(
            SendAsync(
                AdminToken,
                HttpMethod.Post,
                $"/api/workspaces/{workspaceId}/members",
                new { username = outsider.Username, role = "Member" }),
            SendAsync(
                AdminToken,
                HttpMethod.Post,
                $"/api/workspaces/{workspaceId}/members",
                new { username = outsider.Username, role = "Member" }));

        try
        {
            Assert.Single(duplicateAdds, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Single(duplicateAdds, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var response in duplicateAdds)
            {
                response.Dispose();
            }
        }

        using var visible = await SendAsync(outsider.Token, HttpMethod.Get, $"/api/boards/{boardId}");
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);

        using var members = await SendAsync(
            AdminToken,
            HttpMethod.Get,
            $"/api/workspaces/{workspaceId}/members");
        members.EnsureSuccessStatusCode();
        var memberBody = await members.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(
            memberBody.EnumerateArray(),
            member => member.GetProperty("userId").GetGuid() == outsider.UserId);
    }

    [Fact]
    public async Task Board_creation_queued_behind_member_removal_rechecks_authorization_after_the_lock()
    {
        var workspaceId = await CreateWorkspaceAsync("Create authorization race");
        var manager = await Application.CreateUserScenarioAsync("create-manager");
        using (var addManager = await SendAsync(
                   AdminToken,
                   HttpMethod.Post,
                   $"/api/workspaces/{workspaceId}/members",
                   new { username = manager.Username, role = "Admin" }))
        {
            Assert.Equal(HttpStatusCode.Created, addManager.StatusCode);
        }

        using var lockScope = Application.Services.CreateScope();
        var lockDb = lockScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var lockTransaction = await lockDb.Database.BeginTransactionAsync();
        await WorkspaceInvariantLock.AcquireAsync(lockDb, workspaceId);

        var removalTask = SendAsync(
            AdminToken,
            HttpMethod.Delete,
            $"/api/workspaces/{workspaceId}/members/{manager.UserId}");
        Task<HttpResponseMessage>? createTask = null;
        try
        {
            await WaitForBlockedAdvisoryLockAsync(WorkspaceLockNamespace, workspaceId);
            createTask = SendAsync(
                manager.Token,
                HttpMethod.Post,
                $"/api/workspaces/{workspaceId}/boards",
                new { name = "Revoked board", description = "", color = "#2563eb" });
            await WaitForBlockedAdvisoryLockAsync(
                WorkspaceLockNamespace,
                workspaceId,
                expectedCount: 2);
        }
        catch
        {
            await lockTransaction.RollbackAsync();
            await DrainResponseAsync(removalTask);
            await DrainResponseAsync(createTask);
            throw;
        }

        await lockTransaction.RollbackAsync();
        using var removal = await removalTask;
        using var create = await createTask;

        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);

        using var boards = await SendAsync(
            AdminToken,
            HttpMethod.Get,
            $"/api/workspaces/{workspaceId}/boards");
        boards.EnsureSuccessStatusCode();
        Assert.Empty((await boards.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
    }

    [Fact]
    public async Task Board_update_queued_behind_admin_demotion_rechecks_authorization_after_both_locks()
    {
        var workspaceId = await CreateWorkspaceAsync("Update authorization race");
        var boardId = (await CreateBoardAsync(workspaceId, "Original board"))
            .GetProperty("id").GetGuid();
        var manager = await Application.CreateUserScenarioAsync("update-manager");
        using (var addManager = await SendAsync(
                   AdminToken,
                   HttpMethod.Post,
                   $"/api/workspaces/{workspaceId}/members",
                   new { username = manager.Username, role = "Admin" }))
        {
            Assert.Equal(HttpStatusCode.Created, addManager.StatusCode);
        }

        using var lockScope = Application.Services.CreateScope();
        var lockDb = lockScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var lockTransaction = await lockDb.Database.BeginTransactionAsync();
        await WorkspaceInvariantLock.AcquireAsync(lockDb, workspaceId);

        var demotionTask = SendAsync(
            AdminToken,
            HttpMethod.Put,
            $"/api/workspaces/{workspaceId}/members/{manager.UserId}/role",
            new { role = "Member" });
        Task<HttpResponseMessage>? updateTask = null;
        try
        {
            await WaitForBlockedAdvisoryLockAsync(WorkspaceLockNamespace, workspaceId);
            updateTask = SendAsync(
                manager.Token,
                HttpMethod.Put,
                $"/api/boards/{boardId}",
                new { name = "Revoked update", description = "Must not persist", color = "#dc2626" });
            await WaitForBlockedAdvisoryLockAsync(
                WorkspaceLockNamespace,
                workspaceId,
                expectedCount: 2);
        }
        catch
        {
            await lockTransaction.RollbackAsync();
            await DrainResponseAsync(demotionTask);
            await DrainResponseAsync(updateTask);
            throw;
        }

        await lockTransaction.RollbackAsync();
        using var demotion = await demotionTask;
        using var update = await updateTask;

        Assert.Equal(HttpStatusCode.OK, demotion.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);

        var persisted = await GetBoardAsync(boardId);
        Assert.Equal("Original board", persisted.GetProperty("name").GetString());
        Assert.Equal("Integration test board", persisted.GetProperty("description").GetString());
        Assert.Equal("#2563eb", persisted.GetProperty("color").GetString());

        using var memberView = await SendAsync(manager.Token, HttpMethod.Get, $"/api/boards/{boardId}");
        memberView.EnsureSuccessStatusCode();
        Assert.Equal(
            "Member",
            (await memberView.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("role").GetString());
    }

    [Fact]
    public async Task Archived_boards_reject_every_task_mutation_until_restored()
    {
        var workspaceId = await CreateWorkspaceAsync("Archive invariants");
        var boardId = (await CreateBoardAsync(workspaceId, "Archive me"))
            .GetProperty("id").GetGuid();
        var task = await CreateTaskAsync(
            boardId,
            new
            {
                title = "Protected task",
                description = "Must not change while archived",
                priority = "High",
                dueDate = "2030-01-02",
            });
        var taskId = task.GetProperty("id").GetGuid();

        using (var archive = await SendAsync(
                   AdminToken,
                   HttpMethod.Put,
                   $"/api/boards/{boardId}/archive",
                   new { isArchived = true }))
        {
            Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        }

        var blockedMutations = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Post, $"/api/boards/{boardId}/tasks", new { title = "Too late" }),
            (HttpMethod.Put, $"/api/tasks/{taskId}", new
            {
                title = "Changed",
                description = "Changed",
                priority = "Urgent",
                dueDate = "2031-01-01",
                assigneeId = (Guid?)null,
            }),
            (HttpMethod.Put, $"/api/tasks/{taskId}/status", new { status = "Done" }),
            (HttpMethod.Delete, $"/api/tasks/{taskId}", null),
        };

        foreach (var (method, path, body) in blockedMutations)
        {
            using var blocked = await SendAsync(AdminToken, method, path, body);
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        }

        var archived = await GetBoardAsync(boardId);
        Assert.True(archived.GetProperty("isArchived").GetBoolean());
        Assert.Equal(1, archived.GetProperty("taskCount").GetInt32());
        var unchanged = archived.GetProperty("tasks").EnumerateArray().Single();
        Assert.Equal("Protected task", unchanged.GetProperty("title").GetString());
        Assert.Equal("Todo", unchanged.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, unchanged.GetProperty("completedAt").ValueKind);

        using (var stats = await SendAsync(
                   AdminToken,
                   HttpMethod.Get,
                   $"/api/boards/{boardId}/stats"))
        {
            stats.EnsureSuccessStatusCode();
            var body = await stats.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, body.GetProperty("todo").GetInt32());
            Assert.Equal(1, body.GetProperty("total").GetInt32());
        }

        using (var restore = await SendAsync(
                   AdminToken,
                   HttpMethod.Put,
                   $"/api/boards/{boardId}/archive",
                   new { isArchived = false }))
        {
            Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        }

        using (var update = await SendAsync(
                   AdminToken,
                   HttpMethod.Put,
                   $"/api/tasks/{taskId}",
                   new
                   {
                       title = "Restored task",
                       description = "Writes work again",
                       priority = "Urgent",
                       dueDate = (string?)null,
                       assigneeId = (Guid?)null,
                   }))
        {
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        }

        using (var complete = await SendAsync(
                   AdminToken,
                   HttpMethod.Put,
                   $"/api/tasks/{taskId}/status",
                   new { status = "Done" }))
        {
            Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
            Assert.Equal(
                JsonValueKind.String,
                (await complete.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("completedAt").ValueKind);
        }

        using var delete = await SendAsync(AdminToken, HttpMethod.Delete, $"/api/tasks/{taskId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty((await GetBoardAsync(boardId)).GetProperty("tasks").EnumerateArray());
    }

    [Fact]
    public async Task A_create_queued_behind_archive_is_rejected_after_the_archive_boundary()
    {
        var workspaceId = await CreateWorkspaceAsync("Archive race");
        var boardId = (await CreateBoardAsync(workspaceId, "Race board"))
            .GetProperty("id").GetGuid();

        using var lockScope = Application.Services.CreateScope();
        var lockDb = lockScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var lockTransaction = await lockDb.Database.BeginTransactionAsync();
        await BoardInvariantLock.AcquireAsync(lockDb, boardId);

        var archiveTask = SendAsync(
            AdminToken,
            HttpMethod.Put,
            $"/api/boards/{boardId}/archive",
            new { isArchived = true });
        Task<HttpResponseMessage>? createTask = null;
        try
        {
            await WaitForBlockedAdvisoryLockAsync(BoardLockNamespace, boardId);
            createTask = SendAsync(
                AdminToken,
                HttpMethod.Post,
                $"/api/boards/{boardId}/tasks",
                new { title = "Queued behind archive" });
            await WaitForBlockedAdvisoryLockAsync(WorkspaceLockNamespace, workspaceId);
        }
        catch
        {
            await lockTransaction.RollbackAsync();
            await DrainResponseAsync(archiveTask);
            await DrainResponseAsync(createTask);
            throw;
        }

        await lockTransaction.RollbackAsync();
        using var archive = await archiveTask;
        using var create = await createTask;

        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);

        var board = await GetBoardAsync(boardId);
        Assert.True(board.GetProperty("isArchived").GetBoolean());
        Assert.Empty(board.GetProperty("tasks").EnumerateArray());
    }

    private async Task WaitForBlockedAdvisoryLockAsync(
        int lockNamespace,
        Guid resourceId,
        int expectedCount = 1)
    {
        var lockKey = EntityInvariantLock.GetKey(lockNamespace, resourceId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            while (true)
            {
                using var scope = Application.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var blocked = await db.Database
                    .SqlQuery<int>($"""
                        SELECT COUNT(*)::int AS "Value"
                        FROM pg_locks
                        WHERE locktype = 'advisory'
                          AND NOT granted
                          AND classid::bigint = {(long)(uint)(lockKey >> 32)}
                          AND objid::bigint = {(long)(uint)lockKey}
                          AND objsubid = 1
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
                $"Expected {expectedCount} blocked PostgreSQL advisory lock request(s) " +
                $"in namespace {lockNamespace} " +
                $"for resource {resourceId}.",
                exception);
        }
    }

    private static async Task DrainResponseAsync(Task<HttpResponseMessage>? responseTask)
    {
        if (responseTask is null)
        {
            return;
        }

        try
        {
            using var response = await responseTask.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch
        {
            // Preserve the coordination failure that triggered cleanup.
        }
    }

    private async Task<Guid> CreateWorkspaceAsync(string prefix)
    {
        using var response = await SendAsync(
            AdminToken,
            HttpMethod.Post,
            "/api/workspaces",
            new { name = $"{prefix} {Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> CreateBoardAsync(
        Guid workspaceId,
        string name,
        string description = "Integration test board",
        string color = "#2563eb")
    {
        using var response = await SendAsync(
            AdminToken,
            HttpMethod.Post,
            $"/api/workspaces/{workspaceId}/boards",
            new { name, description, color });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal($"/api/boards/{body.GetProperty("id").GetGuid()}", response.Headers.Location?.ToString());
        return body;
    }

    private async Task<JsonElement> CreateTaskAsync(Guid boardId, object body)
    {
        using var response = await SendAsync(
            AdminToken,
            HttpMethod.Post,
            $"/api/boards/{boardId}/tasks",
            body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> GetBoardAsync(Guid boardId)
    {
        using var response = await SendAsync(AdminToken, HttpMethod.Get, $"/api/boards/{boardId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HttpResponseMessage> SendAsync(
        string token,
        HttpMethod method,
        string path,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await Client.SendAsync(request);
    }
}
