using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Api.Infrastructure.Caching;
using TaskFlow.Api.Infrastructure.Persistence;
using TaskFlow.Api.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Infrastructure.Caching;

[Collection(TaskFlowApiCollection.Name)]
public class BoardCacheIntegrationTests(
    TaskFlowTestEnvironment environment,
    ITestOutputHelper output)
    : IntegrationTestBase(environment, output, useRedis: true, authenticateAsAdmin: true)
{
    private string AdminToken => Client.DefaultRequestHeaders.Authorization!.Parameter!;

    [Fact]
    public async Task Reordering_a_task_invalidates_the_cached_board_order()
    {
        var workspaceId = await CreateWorkspaceAsync("Cached task order");
        var boardId = await CreateBoardAsync(workspaceId, "Reorder cache");
        var first = await CreateTaskAsync(boardId, "First task");
        var second = await CreateTaskAsync(boardId, "Second task");
        var firstId = first.GetProperty("id").GetGuid();
        var secondId = second.GetProperty("id").GetGuid();
        await AssertMissThenHitAsync($"/api/boards/{boardId}");

        using var move = await SendAsync(
            AdminToken,
            HttpMethod.Put,
            $"/api/tasks/{secondId}/status",
            new { status = "Todo", placement = "before", beforeTaskId = firstId });
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);

        foreach (var expectedCache in new[] { "MISS", "HIT" })
        {
            using var response = await SendAsync(AdminToken, HttpMethod.Get, $"/api/boards/{boardId}");
            response.EnsureSuccessStatusCode();
            Assert.Equal(expectedCache, response.Headers.GetValues("X-Cache").Single());
            var board = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(
                [secondId, firstId],
                board.GetProperty("tasks").EnumerateArray().Select(task => task.GetProperty("id").GetGuid()));
        }
    }

    [Fact]
    public async Task Board_and_stats_cache_invalidation_covers_every_mutation_type()
    {
        var workspaceId = await CreateWorkspaceAsync("Cache mutation matrix");
        var boardId = await CreateBoardAsync(workspaceId, "Cache matrix");

        await AssertMissThenHitAsync($"/api/boards/{boardId}");
        await AssertMissThenHitAsync($"/api/boards/{boardId}/stats");

        var task = await CreateTaskAsync(boardId, "Cache task");
        var taskId = task.GetProperty("id").GetGuid();
        await AssertMissThenHitAsync($"/api/boards/{boardId}", "Cache task");
        await AssertMissThenHitAsync($"/api/boards/{boardId}/stats", "\"total\":1");

        using (var updateTask = await SendAsync(
                   AdminToken,
                   HttpMethod.Put,
                   $"/api/tasks/{taskId}",
                   new
                   {
                       title = "Updated cache task",
                       description = "Updated",
                       priority = "High",
                       dueDate = (string?)null,
                       assigneeId = (Guid?)null,
                   }))
        {
            Assert.Equal(HttpStatusCode.OK, updateTask.StatusCode);
        }
        await AssertMissThenHitAsync($"/api/boards/{boardId}", "Updated cache task");
        await AssertMissThenHitAsync($"/api/boards/{boardId}/stats", "\"total\":1");

        using (var moveTask = await SendAsync(
                   AdminToken,
                   HttpMethod.Put,
                   $"/api/tasks/{taskId}/status",
                   new { status = "Done" }))
        {
            Assert.Equal(HttpStatusCode.OK, moveTask.StatusCode);
        }
        await AssertMissThenHitAsync($"/api/boards/{boardId}", "\"status\":\"Done\"");
        await AssertMissThenHitAsync($"/api/boards/{boardId}/stats", "\"done\":1");

        using (var updateBoard = await SendAsync(
                   AdminToken,
                   HttpMethod.Put,
                   $"/api/boards/{boardId}",
                   new { name = "Renamed cache matrix", description = "", color = "#059669" }))
        {
            Assert.Equal(HttpStatusCode.OK, updateBoard.StatusCode);
        }

        // Board management endpoints return a full board and intentionally repopulate
        // that cache entry. Stats are invalidated but are not needed for the response.
        await AssertHitAsync($"/api/boards/{boardId}", "Renamed cache matrix");
        await AssertMissThenHitAsync($"/api/boards/{boardId}/stats", "\"done\":1");

        using (var archive = await SendAsync(
                   AdminToken,
                   HttpMethod.Put,
                   $"/api/boards/{boardId}/archive",
                   new { isArchived = true }))
        {
            Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        }
        await AssertHitAsync($"/api/boards/{boardId}", "\"isArchived\":true");
        await AssertMissThenHitAsync($"/api/boards/{boardId}/stats", "\"done\":1");

        using (var restore = await SendAsync(
                   AdminToken,
                   HttpMethod.Put,
                   $"/api/boards/{boardId}/archive",
                   new { isArchived = false }))
        {
            Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        }
        await AssertHitAsync($"/api/boards/{boardId}", "\"isArchived\":false");
        await AssertMissThenHitAsync($"/api/boards/{boardId}/stats", "\"done\":1");

        using (var delete = await SendAsync(AdminToken, HttpMethod.Delete, $"/api/tasks/{taskId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }
        await AssertMissThenHitAsync($"/api/boards/{boardId}", "\"taskCount\":0");
        await AssertMissThenHitAsync($"/api/boards/{boardId}/stats", "\"total\":0");
    }

    [Fact]
    public async Task Corrupt_cached_JSON_falls_back_to_Postgres_and_repairs_the_entry()
    {
        var workspaceId = await CreateWorkspaceAsync("Cache corruption");
        var boardId = await CreateBoardAsync(workspaceId, "Authoritative Postgres board");
        var cache = Application.Services.GetRequiredService<BoardCache>();

        await cache.InvalidateAsync(boardId);
        var (_, _, boardGeneration) = await cache.GetVersionedAsync(
            boardId,
            BoardCache.BoardKey(boardId));
        Assert.True(await cache.SetIfGenerationUnchangedAsync(
            boardId,
            BoardCache.BoardKey(boardId),
            "{not-valid-json",
            boardGeneration,
            TimeSpan.FromMinutes(1)));

        using (var repairedBoard = await SendAsync(
                   AdminToken,
                   HttpMethod.Get,
                   $"/api/boards/{boardId}"))
        {
            Assert.Equal(HttpStatusCode.OK, repairedBoard.StatusCode);
            Assert.Equal("MISS", CacheHeader(repairedBoard));
            Assert.Equal(
                "Authoritative Postgres board",
                (await repairedBoard.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("name").GetString());
        }
        await AssertHitAsync($"/api/boards/{boardId}", "Authoritative Postgres board");

        var (_, _, statsGeneration) = await cache.GetVersionedAsync(
            boardId,
            BoardCache.StatsKey(boardId));
        Assert.True(await cache.SetIfGenerationUnchangedAsync(
            boardId,
            BoardCache.StatsKey(boardId),
            "[broken-stats",
            statsGeneration,
            TimeSpan.FromMinutes(1)));

        using (var repairedStats = await SendAsync(
                   AdminToken,
                   HttpMethod.Get,
                   $"/api/boards/{boardId}/stats"))
        {
            Assert.Equal(HttpStatusCode.OK, repairedStats.StatusCode);
            Assert.Equal("MISS", CacheHeader(repairedStats));
            Assert.Equal(
                0,
                (await repairedStats.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("total").GetInt32());
        }
        await AssertHitAsync($"/api/boards/{boardId}/stats", "\"total\":0");
    }

    [Fact]
    public async Task Activity_is_board_scoped_ordered_and_trimmed_to_the_latest_twenty_events()
    {
        var workspaceId = await CreateWorkspaceAsync("Activity streams");
        var activeBoardId = await CreateBoardAsync(workspaceId, "Active stream");
        var quietBoardId = await CreateBoardAsync(workspaceId, "Quiet stream");
        var task = await CreateTaskAsync(activeBoardId, "Activity task");
        var taskId = task.GetProperty("id").GetGuid();

        using (var update = await SendAsync(
                   AdminToken,
                   HttpMethod.Put,
                   $"/api/tasks/{taskId}",
                   new
                   {
                       title = "Renamed activity task",
                       description = "",
                       priority = "Medium",
                       dueDate = (string?)null,
                       assigneeId = (Guid?)null,
                   }))
        {
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        }
        using (var move = await SendAsync(
                   AdminToken,
                   HttpMethod.Put,
                   $"/api/tasks/{taskId}/status",
                   new { status = "Doing" }))
        {
            Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        }
        using (var delete = await SendAsync(AdminToken, HttpMethod.Delete, $"/api/tasks/{taskId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        var activity = await GetActivityAsync(activeBoardId);
        Assert.True(activity.GetProperty("available").GetBoolean());
        Assert.Equal(
            [
                "Deleted \"Renamed activity task\"",
                "Moved \"Renamed activity task\" to Doing",
                "Updated \"Renamed activity task\"",
                "Created \"Activity task\"",
            ],
            activity.GetProperty("entries").EnumerateArray()
                .Select(entry => entry.GetProperty("message").GetString()));

        var quietActivity = await GetActivityAsync(quietBoardId);
        Assert.True(quietActivity.GetProperty("available").GetBoolean());
        Assert.Empty(quietActivity.GetProperty("entries").EnumerateArray());

        var cappedBoardId = await CreateBoardAsync(workspaceId, "Capped stream");
        for (var index = 0; index < 22; index++)
        {
            await CreateTaskAsync(cappedBoardId, $"Event {index:D2}");
        }

        var capped = await GetActivityAsync(cappedBoardId);
        var messages = capped.GetProperty("entries").EnumerateArray()
            .Select(entry => entry.GetProperty("message").GetString()).ToArray();
        Assert.Equal(20, messages.Length);
        Assert.Equal("Created \"Event 21\"", messages[0]);
        Assert.Equal("Created \"Event 02\"", messages[^1]);
        Assert.DoesNotContain("Created \"Event 01\"", messages);
        Assert.DoesNotContain("Created \"Event 00\"", messages);
    }

    [Fact]
    public async Task Redis_outage_keeps_board_writes_reads_and_stats_available_from_Postgres()
    {
        var workspaceId = await CreateWorkspaceAsync("Cache outage");
        var boardId = await CreateBoardAsync(workspaceId, "Available without Redis");
        using var degradedFactory = Application.WithWebHostBuilder(builder =>
            builder.UseSetting(
                "ConnectionStrings:Redis",
                "127.0.0.1:1,connectTimeout=200,connectRetry=0"));
        using var degradedClient = degradedFactory.CreateIntegrationClient();
        degradedClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AdminToken);

        using (var board = await degradedClient.GetAsync($"/api/boards/{boardId}"))
        {
            Assert.Equal(HttpStatusCode.OK, board.StatusCode);
            Assert.Equal("DOWN", CacheHeader(board));
        }
        using (var stats = await degradedClient.GetAsync($"/api/boards/{boardId}/stats"))
        {
            Assert.Equal(HttpStatusCode.OK, stats.StatusCode);
            Assert.Equal("DOWN", CacheHeader(stats));
        }
        using (var activity = await degradedClient.GetAsync($"/api/boards/{boardId}/activity"))
        {
            Assert.Equal(HttpStatusCode.OK, activity.StatusCode);
            var body = await activity.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(body.GetProperty("available").GetBoolean());
            Assert.Empty(body.GetProperty("entries").EnumerateArray());
        }

        using (var create = await degradedClient.PostAsJsonAsync(
                   $"/api/boards/{boardId}/tasks",
                   new { title = "Persist during outage" }))
        {
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        }
        using (var persisted = await degradedClient.GetAsync($"/api/boards/{boardId}"))
        {
            Assert.Equal(HttpStatusCode.OK, persisted.StatusCode);
            Assert.Equal("DOWN", CacheHeader(persisted));
            Assert.Contains("Persist during outage", await persisted.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Healthy_instance_rejects_board_and_stats_retained_across_repeated_partitioned_writes()
    {
        var workspaceId = await CreateWorkspaceAsync("Retained stale cache");
        var boardId = await CreateBoardAsync(workspaceId, "Cross-instance cache recovery");
        await SetBoardUpdatedAtAsync(boardId, DateTimeOffset.UtcNow.AddDays(1));
        var initialState = await ReadBoardStateAsync(boardId);
        await AssertMissThenHitAsync($"/api/boards/{boardId}", "Cross-instance cache recovery");
        await AssertMissThenHitAsync($"/api/boards/{boardId}/stats", "\"total\":0");

        using var degradedFactory = Application.WithWebHostBuilder(builder =>
            builder.UseSetting(
                "ConnectionStrings:Redis",
                "127.0.0.1:1,connectTimeout=200,connectRetry=0"));
        using var degradedClient = degradedFactory.CreateIntegrationClient();
        degradedClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AdminToken);

        using (var create = await degradedClient.PostAsJsonAsync(
                   $"/api/boards/{boardId}/tasks",
                   new { title = "Committed without invalidation" }))
        {
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        }
        using (var create = await degradedClient.PostAsJsonAsync(
                   $"/api/boards/{boardId}/tasks",
                   new { title = "Second commit without invalidation" }))
        {
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        }

        var committedState = await ReadBoardStateAsync(boardId);
        Assert.Equal(initialState.Revision + 2, committedState.Revision);
        Assert.Equal(initialState.UpdatedAt, committedState.UpdatedAt);
        Assert.Equal(2, committedState.TaskCount);

        // The failed writers cannot advance Redis' generation or remove either value.
        // UpdatedAt was deliberately placed in the future, so it stays identical across
        // both commits. Only the monotonic board revision can reject these stale entries.
        var healthyCache = Application.Services.GetRequiredService<BoardCache>();
        var (boardStatus, retainedBoard, _) = await healthyCache.GetVersionedAsync(
            boardId,
            BoardCache.BoardKey(boardId));
        var (statsStatus, retainedStats, _) = await healthyCache.GetVersionedAsync(
            boardId,
            BoardCache.StatsKey(boardId));
        Assert.Equal(CacheStatus.Hit, boardStatus);
        Assert.Equal(CacheStatus.Hit, statsStatus);
        Assert.NotNull(retainedBoard);
        Assert.NotNull(retainedStats);
        Assert.DoesNotContain("Committed without invalidation", retainedBoard);
        Assert.DoesNotContain("Second commit without invalidation", retainedBoard);
        Assert.Contains("\"total\":0", retainedStats);
        using (var retainedBoardDocument = JsonDocument.Parse(retainedBoard))
        using (var retainedStatsDocument = JsonDocument.Parse(retainedStats))
        {
            Assert.Equal(
                initialState.Revision,
                retainedBoardDocument.RootElement.GetProperty("boardRevision").GetInt64());
            Assert.Equal(
                initialState.Revision,
                retainedStatsDocument.RootElement.GetProperty("boardRevision").GetInt64());
            Assert.Equal(
                initialState.UpdatedAt,
                retainedBoardDocument.RootElement
                    .GetProperty("board")
                    .GetProperty("updatedAt")
                    .GetDateTimeOffset());
        }

        using (var board = await Client.GetAsync($"/api/boards/{boardId}"))
        {
            Assert.Equal(HttpStatusCode.OK, board.StatusCode);
            Assert.Equal("MISS", CacheHeader(board));
            var body = await board.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(
                "Committed without invalidation",
                body.GetProperty("tasks").EnumerateArray()
                    .Select(task => task.GetProperty("title").GetString()));
            Assert.Contains(
                "Second commit without invalidation",
                body.GetProperty("tasks").EnumerateArray()
                    .Select(task => task.GetProperty("title").GetString()));
            Assert.False(body.TryGetProperty("revision", out _));
            Assert.False(body.TryGetProperty("boardRevision", out _));
        }
        using (var stats = await Client.GetAsync($"/api/boards/{boardId}/stats"))
        {
            Assert.Equal(HttpStatusCode.OK, stats.StatusCode);
            Assert.Equal("MISS", CacheHeader(stats));
            Assert.Contains("\"total\":2", await stats.Content.ReadAsStringAsync());
        }

        await AssertHitAsync($"/api/boards/{boardId}", "Committed without invalidation");
        await AssertHitAsync($"/api/boards/{boardId}/stats", "\"total\":2");

        var (_, repairedBoard, _) = await healthyCache.GetVersionedAsync(
            boardId,
            BoardCache.BoardKey(boardId));
        var (_, repairedStats, _) = await healthyCache.GetVersionedAsync(
            boardId,
            BoardCache.StatsKey(boardId));
        using (var repairedBoardDocument = JsonDocument.Parse(repairedBoard!))
        using (var repairedStatsDocument = JsonDocument.Parse(repairedStats!))
        {
            Assert.Equal(
                committedState.Revision,
                repairedBoardDocument.RootElement.GetProperty("boardRevision").GetInt64());
            Assert.Equal(
                committedState.Revision,
                repairedStatsDocument.RootElement.GetProperty("boardRevision").GetInt64());
        }
    }

    [Fact]
    public async Task Healthy_instance_rejects_retained_board_after_profile_update_changes_cached_task_names()
    {
        var workspaceId = await CreateWorkspaceAsync("Profile cache recovery");
        var boardId = await CreateBoardAsync(workspaceId, "Profile cache board");
        var member = await Application.CreateUserScenarioAsync("cache-profile");

        using (var addMember = await SendAsync(
                   AdminToken,
                   HttpMethod.Post,
                   $"/api/workspaces/{workspaceId}/members",
                   new { member.Username, role = "Member" }))
        {
            Assert.Equal(HttpStatusCode.Created, addMember.StatusCode);
        }
        using (var createTask = await SendAsync(
                   AdminToken,
                   HttpMethod.Post,
                   $"/api/boards/{boardId}/tasks",
                   new
                   {
                       title = "Assigned profile task",
                       description = "",
                       priority = "Medium",
                       assigneeId = member.UserId,
                   }))
        {
            Assert.Equal(HttpStatusCode.Created, createTask.StatusCode);
        }
        await AssertMissThenHitAsync($"/api/boards/{boardId}", member.DisplayName);

        using var degradedFactory = Application.WithWebHostBuilder(builder =>
            builder.UseSetting(
                "ConnectionStrings:Redis",
                "127.0.0.1:1,connectTimeout=200,connectRetry=0"));
        using var degradedClient = degradedFactory.CreateIntegrationClient();
        degradedClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", member.Token);
        const string updatedDisplayName = "Updated Cache Profile";
        using (var update = await degradedClient.PutAsJsonAsync(
                   "/api/users/me",
                   new
                   {
                       displayName = updatedDisplayName,
                       email = $"{member.Username}@example.test",
                   }))
        {
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        }

        var healthyCache = Application.Services.GetRequiredService<BoardCache>();
        var (retainedStatus, retainedBoard, _) = await healthyCache.GetVersionedAsync(
            boardId,
            BoardCache.BoardKey(boardId));
        Assert.Equal(CacheStatus.Hit, retainedStatus);
        Assert.NotNull(retainedBoard);
        Assert.Contains(member.DisplayName, retainedBoard);
        Assert.DoesNotContain(updatedDisplayName, retainedBoard);

        using (var board = await Client.GetAsync($"/api/boards/{boardId}"))
        {
            Assert.Equal(HttpStatusCode.OK, board.StatusCode);
            Assert.Equal("MISS", CacheHeader(board));
            Assert.Contains(updatedDisplayName, await board.Content.ReadAsStringAsync());
        }
        await AssertHitAsync($"/api/boards/{boardId}", updatedDisplayName);
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

    private async Task<Guid> CreateBoardAsync(Guid workspaceId, string name)
    {
        using var response = await SendAsync(
            AdminToken,
            HttpMethod.Post,
            $"/api/workspaces/{workspaceId}/boards",
            new { name, description = "Cache integration board", color = "#2563eb" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> CreateTaskAsync(Guid boardId, string title)
    {
        using var response = await SendAsync(
            AdminToken,
            HttpMethod.Post,
            $"/api/boards/{boardId}/tasks",
            new { title });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> GetActivityAsync(Guid boardId)
    {
        using var response = await SendAsync(
            AdminToken,
            HttpMethod.Get,
            $"/api/boards/{boardId}/activity");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task SetBoardUpdatedAtAsync(Guid boardId, DateTimeOffset updatedAt)
    {
        using var scope = Application.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var board = await db.Boards.SingleAsync(candidate => candidate.Id == boardId);
        board.UpdatedAt = updatedAt;
        await db.SaveChangesAsync();
    }

    private async Task<(long Revision, DateTimeOffset UpdatedAt, int TaskCount)> ReadBoardStateAsync(
        Guid boardId)
    {
        using var scope = Application.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var state = await db.Boards
            .AsNoTracking()
            .Where(board => board.Id == boardId)
            .Select(board => new
            {
                board.Revision,
                board.UpdatedAt,
                TaskCount = board.Tasks.Count,
            })
            .SingleAsync();
        return (state.Revision, state.UpdatedAt, state.TaskCount);
    }

    private async Task AssertMissThenHitAsync(string path, string? expectedContent = null)
    {
        using var miss = await SendAsync(AdminToken, HttpMethod.Get, path);
        using var hit = await SendAsync(AdminToken, HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, miss.StatusCode);
        Assert.Equal(HttpStatusCode.OK, hit.StatusCode);
        Assert.Equal("MISS", CacheHeader(miss));
        Assert.Equal("HIT", CacheHeader(hit));
        var missBody = await miss.Content.ReadAsStringAsync();
        var hitBody = await hit.Content.ReadAsStringAsync();
        Assert.Equal(missBody, hitBody);
        if (expectedContent is not null)
        {
            Assert.Contains(expectedContent, hitBody);
        }
    }

    private async Task AssertHitAsync(string path, string expectedContent)
    {
        using var response = await SendAsync(AdminToken, HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("HIT", CacheHeader(response));
        Assert.Contains(expectedContent, await response.Content.ReadAsStringAsync());
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

    private static string CacheHeader(HttpResponseMessage response) =>
        response.Headers.GetValues("X-Cache").Single();
}
