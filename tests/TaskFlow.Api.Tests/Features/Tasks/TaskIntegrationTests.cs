using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TaskFlow.Api.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Features.Tasks;

[Collection(TaskFlowApiCollection.Name)]
public class TaskIntegrationTests(
    TaskFlowTestEnvironment environment,
    ITestOutputHelper output)
    : IntegrationTestBase(environment, output, authenticateAsAdmin: true)
{
    private string AdminToken => Client.DefaultRequestHeaders.Authorization!.Parameter!;

    [Fact]
    public async Task Creating_a_task_trims_its_title_and_adds_it_only_to_the_selected_board()
    {
        var workspaceId = await CreateWorkspaceAsync("Task destination");
        var firstBoardId = (await CreateBoardAsync(workspaceId, "Other board"))
            .GetProperty("id").GetGuid();
        var selectedBoardId = (await CreateBoardAsync(workspaceId, "Selected board"))
            .GetProperty("id").GetGuid();

        var created = await CreateTaskAsync(selectedBoardId, new { title = "  Write the tests  " });
        Assert.Equal("Write the tests", created.GetProperty("title").GetString());
        Assert.Equal("Todo", created.GetProperty("status").GetString());

        var selected = await GetBoardAsync(selectedBoardId);
        var task = Assert.Single(selected.GetProperty("tasks").EnumerateArray());
        Assert.Equal(created.GetProperty("id").GetGuid(), task.GetProperty("id").GetGuid());
        Assert.Equal("Write the tests", task.GetProperty("title").GetString());
        Assert.Empty((await GetBoardAsync(firstBoardId)).GetProperty("tasks").EnumerateArray());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"title\":null}")]
    [InlineData("{\"title\":\"   \"}")]
    public async Task Creating_a_task_requires_a_nonblank_title_without_changing_the_board(string json)
    {
        var workspaceId = await CreateWorkspaceAsync("Required task title");
        var boardId = (await CreateBoardAsync(workspaceId, "Validation board"))
            .GetProperty("id").GetGuid();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await Client.PostAsync($"/api/boards/{boardId}/tasks", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await GetBoardAsync(boardId)).GetProperty("tasks").EnumerateArray());
    }

    [Fact]
    public async Task Moving_a_missing_task_returns_404()
    {
        using var response = await Client.PutAsJsonAsync(
            $"/api/tasks/{Guid.NewGuid()}/status", new { status = "Doing" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"status\":0}")]
    [InlineData("{\"status\":\"Blocked\"}")]
    public async Task Moving_a_task_requires_a_named_known_status_without_changing_it(string json)
    {
        var workspaceId = await CreateWorkspaceAsync("Required task status");
        var boardId = (await CreateBoardAsync(workspaceId, "Status validation"))
            .GetProperty("id").GetGuid();
        var task = await CreateTaskAsync(boardId, new { title = "Keep my status" });
        var taskId = task.GetProperty("id").GetGuid();
        var before = (await GetBoardAsync(boardId)).GetRawText();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await Client.PutAsync($"/api/tasks/{taskId}/status", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, (await GetBoardAsync(boardId)).GetRawText());
    }

    [Fact]
    public async Task Deleting_a_task_removes_it_from_the_board_and_returns_204_then_404()
    {
        var workspaceId = await CreateWorkspaceAsync("Task deletion");
        var boardId = (await CreateBoardAsync(workspaceId, "Delete from here"))
            .GetProperty("id").GetGuid();
        var task = await CreateTaskAsync(boardId, new { title = "Delete me" });
        var taskId = task.GetProperty("id").GetGuid();

        using var deleted = await Client.DeleteAsync($"/api/tasks/{taskId}");
        using var deletedAgain = await Client.DeleteAsync($"/api/tasks/{taskId}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deletedAgain.StatusCode);
        Assert.Empty((await GetBoardAsync(boardId)).GetProperty("tasks").EnumerateArray());
    }

    [Fact]
    public async Task Parallel_task_creation_allocates_stable_contiguous_positions()
    {
        var workspaceId = await CreateWorkspaceAsync("Parallel positions");
        var boardId = (await CreateBoardAsync(workspaceId, "Concurrent board"))
            .GetProperty("id").GetGuid();
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<HttpResponseMessage> CreateWhenReleasedAsync(int number)
        {
            await start.Task;
            return await SendAsync(
                AdminToken,
                HttpMethod.Post,
                $"/api/boards/{boardId}/tasks",
                new { title = $"Parallel task {number:D2}" });
        }

        var pending = Enumerable.Range(0, 24).Select(CreateWhenReleasedAsync).ToArray();
        start.SetResult(true);
        var responses = await Task.WhenAll(pending);
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        var board = await GetBoardAsync(boardId);
        var tasks = board.GetProperty("tasks").EnumerateArray().ToArray();
        Assert.Equal(24, board.GetProperty("taskCount").GetInt32());
        Assert.Equal(Enumerable.Range(0, 24), tasks.Select(task => task.GetProperty("position").GetInt32()));
        Assert.Equal(
            Enumerable.Range(0, 24).Select(number => $"Parallel task {number:D2}").Order(),
            tasks.Select(task => task.GetProperty("title").GetString()!).Order());
        Assert.Equal(24, tasks.Select(task => task.GetProperty("id").GetGuid()).Distinct().Count());
        Assert.True(
            board.GetProperty("updatedAt").GetDateTimeOffset() >=
            tasks.Max(task => task.GetProperty("createdAt").GetDateTimeOffset()));
    }

    [Fact]
    public async Task Task_limits_rich_fields_and_reporting_transitions_are_consistent()
    {
        var workspaceId = await CreateWorkspaceAsync("Task reporting");
        var boardId = (await CreateBoardAsync(workspaceId, "Metrics board"))
            .GetProperty("id").GetGuid();
        using var profileResponse = await SendAsync(AdminToken, HttpMethod.Get, "/api/users/me");
        profileResponse.EnsureSuccessStatusCode();
        var profile = await profileResponse.Content.ReadFromJsonAsync<JsonElement>();
        var adminId = profile.GetProperty("id").GetGuid();

        var boundaryTask = await CreateTaskAsync(
            boardId,
            new
            {
                title = $"  {new string('x', 200)}  ",
                description = $"  {new string('d', 4_000)}  ",
                priority = "Urgent",
                dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
                assigneeId = adminId,
            });
        Assert.Equal(200, boundaryTask.GetProperty("title").GetString()!.Length);
        Assert.Equal(4_000, boundaryTask.GetProperty("description").GetString()!.Length);
        Assert.Equal("Urgent", boundaryTask.GetProperty("priority").GetString());
        Assert.Equal(adminId, boundaryTask.GetProperty("assigneeId").GetGuid());
        Assert.Equal("TaskFlow Admin", boundaryTask.GetProperty("assigneeName").GetString());
        Assert.Equal("TaskFlow Admin", boundaryTask.GetProperty("createdByName").GetString());

        object[] invalidTasks =
        [
            new { title = new string('x', 201), description = "", priority = "Low" },
            new { title = "Too much detail", description = new string('d', 4_001), priority = "Low" },
            new { title = "Bad priority", description = "", priority = "Critical" },
        ];
        foreach (var invalidTask in invalidTasks)
        {
            using var invalid = await SendAsync(
                AdminToken,
                HttpMethod.Post,
                $"/api/boards/{boardId}/tasks",
                invalidTask);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        var doing = await CreateTaskAsync(boardId, new { title = "Doing task" });
        var done = await CreateTaskAsync(
            boardId,
            new
            {
                title = "Done task",
                dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            });
        await CreateTaskAsync(
            boardId,
            new
            {
                title = "Future task",
                dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            });

        using (var moveDoing = await SendAsync(
                   AdminToken,
                   HttpMethod.Put,
                   $"/api/tasks/{doing.GetProperty("id").GetGuid()}/status",
                   new { status = "Doing" }))
        {
            Assert.Equal(HttpStatusCode.OK, moveDoing.StatusCode);
        }

        var doneId = done.GetProperty("id").GetGuid();
        DateTimeOffset completedAt;
        using (var complete = await SendAsync(
                   AdminToken,
                   HttpMethod.Put,
                   $"/api/tasks/{doneId}/status",
                   new { status = "Done" }))
        {
            Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
            completedAt = (await complete.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("completedAt").GetDateTimeOffset();
        }

        using (var completeAgain = await SendAsync(
                   AdminToken,
                   HttpMethod.Put,
                   $"/api/tasks/{doneId}/status",
                   new { status = "Done" }))
        {
            Assert.Equal(
                completedAt,
                (await completeAgain.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("completedAt").GetDateTimeOffset());
        }

        var stats = await GetStatsAsync(boardId);
        Assert.Equal(2, stats.GetProperty("todo").GetInt32());
        Assert.Equal(1, stats.GetProperty("doing").GetInt32());
        Assert.Equal(1, stats.GetProperty("done").GetInt32());
        Assert.Equal(4, stats.GetProperty("total").GetInt32());
        Assert.Equal(1, stats.GetProperty("doneToday").GetInt32());
        Assert.Equal(1, stats.GetProperty("overdue").GetInt32());
        Assert.InRange(stats.GetProperty("oldestActiveAgeDays").GetDouble(), 0, 1);

        using (var reopen = await SendAsync(
                   AdminToken,
                   HttpMethod.Put,
                   $"/api/tasks/{doneId}/status",
                   new { status = "Todo" }))
        {
            Assert.Equal(JsonValueKind.Null, (await reopen.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("completedAt").ValueKind);
        }

        var reopenedStats = await GetStatsAsync(boardId);
        Assert.Equal(3, reopenedStats.GetProperty("todo").GetInt32());
        Assert.Equal(0, reopenedStats.GetProperty("done").GetInt32());
        Assert.Equal(0, reopenedStats.GetProperty("doneToday").GetInt32());
        Assert.Equal(2, reopenedStats.GetProperty("overdue").GetInt32());
        Assert.Equal(4, (await GetBoardAsync(boardId)).GetProperty("taskCount").GetInt32());
    }

    [Fact]
    public async Task Task_placement_persists_at_column_edges_and_reorders_completed_tasks()
    {
        var workspaceId = await CreateWorkspaceAsync("Task placement");
        var boardId = (await CreateBoardAsync(workspaceId, "Deployment checklist"))
            .GetProperty("id").GetGuid();
        var firstId = (await CreateTaskAsync(boardId, new { title = "Existing completed task" }))
            .GetProperty("id").GetGuid();
        var dnsId = (await CreateTaskAsync(boardId, new { title = "Add a DNS record" }))
            .GetProperty("id").GetGuid();
        var deployId = (await CreateTaskAsync(boardId, new { title = "Deploy" }))
            .GetProperty("id").GetGuid();

        await MoveAsync(firstId, new { status = "Done", placement = "bottom" });
        await MoveAsync(dnsId, new { status = "Done", placement = "top" });
        var completed = await MoveAsync(deployId, new { status = "Done", placement = "top" });
        var completedAt = completed.GetProperty("completedAt").GetDateTimeOffset();
        await AssertDoneOrderAsync(deployId, dnsId, firstId);

        var reordered = await MoveAsync(deployId, new { status = "Done", placement = "bottom" });
        Assert.Equal(completedAt, reordered.GetProperty("completedAt").GetDateTimeOffset());
        await AssertDoneOrderAsync(dnsId, firstId, deployId);

        await MoveAsync(firstId, new { status = "Done", placement = "top" });
        await AssertDoneOrderAsync(firstId, dnsId, deployId);

        // Existing clients may still change only the status without choosing an edge.
        var position = reordered.GetProperty("position").GetInt32();
        var reopened = await MoveAsync(deployId, new { status = "Doing" });
        Assert.Equal(position, reopened.GetProperty("position").GetInt32());
        Assert.Equal(JsonValueKind.Null, reopened.GetProperty("completedAt").ValueKind);
        await AssertDoneOrderAsync(firstId, dnsId);

        async Task<JsonElement> MoveAsync(Guid taskId, object request)
        {
            using var response = await SendAsync(AdminToken, HttpMethod.Put, $"/api/tasks/{taskId}/status", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        async Task AssertDoneOrderAsync(params Guid[] expected)
        {
            var board = await GetBoardAsync(boardId);
            var done = board.GetProperty("tasks").EnumerateArray()
                .Where(task => task.GetProperty("status").GetString() == "Done").ToArray();
            Assert.Equal(expected, done.Select(task => task.GetProperty("id").GetGuid()));
            Assert.Equal(done.Length, done.Select(task => task.GetProperty("position").GetInt32()).Distinct().Count());
        }
    }

    [Fact]
    public async Task Task_placement_before_another_card_persists_between_columns_and_within_the_same_column()
    {
        var workspaceId = await CreateWorkspaceAsync("Insert task");
        var boardId = (await CreateBoardAsync(workspaceId, "Release checklist"))
            .GetProperty("id").GetGuid();
        var firstId = (await CreateTaskAsync(boardId, new { title = "First completed task" }))
            .GetProperty("id").GetGuid();
        var middleId = (await CreateTaskAsync(boardId, new { title = "Middle completed task" }))
            .GetProperty("id").GetGuid();
        var lastId = (await CreateTaskAsync(boardId, new { title = "Last completed task" }))
            .GetProperty("id").GetGuid();
        var incomingId = (await CreateTaskAsync(boardId, new { title = "Add a DNS record" }))
            .GetProperty("id").GetGuid();
        var untouched = await CreateTaskAsync(boardId, new { title = "Still to do" });
        foreach (var taskId in new[] { firstId, middleId, lastId })
        {
            await MoveAsync(taskId, new { status = "Done", placement = "bottom" });
        }

        var beforeMove = await GetBoardAsync(boardId);
        var lastCompletedAt = beforeMove.GetProperty("tasks").EnumerateArray()
            .Single(task => task.GetProperty("id").GetGuid() == lastId)
            .GetProperty("completedAt").GetDateTimeOffset();

        var inserted = await MoveAsync(incomingId, new { status = "Done", placement = "before", beforeTaskId = middleId });
        Assert.Equal(1, inserted.GetProperty("position").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, inserted.GetProperty("completedAt").ValueKind);
        await AssertDoneOrderAsync(firstId, incomingId, middleId, lastId);

        var reordered = await MoveAsync(lastId, new { status = "Done", placement = "before", beforeTaskId = incomingId });
        Assert.Equal(lastCompletedAt, reordered.GetProperty("completedAt").GetDateTimeOffset());
        await AssertDoneOrderAsync(firstId, lastId, incomingId, middleId);

        var remaining = (await GetBoardAsync(boardId)).GetProperty("tasks").EnumerateArray()
            .Single(task => task.GetProperty("status").GetString() == "Todo");
        Assert.Equal(untouched.GetProperty("id").GetGuid(), remaining.GetProperty("id").GetGuid());
        Assert.Equal(untouched.GetProperty("position").GetInt32(), remaining.GetProperty("position").GetInt32());

        async Task<JsonElement> MoveAsync(Guid taskId, object request)
        {
            using var response = await SendAsync(AdminToken, HttpMethod.Put, $"/api/tasks/{taskId}/status", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        async Task AssertDoneOrderAsync(params Guid[] expected)
        {
            var persisted = await GetBoardAsync(boardId);
            var done = persisted.GetProperty("tasks").EnumerateArray()
                .Where(task => task.GetProperty("status").GetString() == "Done").ToArray();
            Assert.Equal(expected, done.Select(task => task.GetProperty("id").GetGuid()));
            Assert.Equal(Enumerable.Range(0, done.Length), done.Select(task => task.GetProperty("position").GetInt32()));
        }
    }

    [Fact]
    public async Task Invalid_or_stale_insertion_anchors_do_not_change_either_board()
    {
        var workspaceId = await CreateWorkspaceAsync("Insertion validation");
        var boardId = (await CreateBoardAsync(workspaceId, "Original board"))
            .GetProperty("id").GetGuid();
        var otherBoardId = (await CreateBoardAsync(workspaceId, "Other board"))
            .GetProperty("id").GetGuid();
        var taskId = (await CreateTaskAsync(boardId, new { title = "Stay here" }))
            .GetProperty("id").GetGuid();
        var wrongStateId = (await CreateTaskAsync(boardId, new { title = "In Todo, not Done" }))
            .GetProperty("id").GetGuid();
        var otherBoardTaskId = (await CreateTaskAsync(otherBoardId, new { title = "Elsewhere" }))
            .GetProperty("id").GetGuid();
        var deletedId = (await CreateTaskAsync(boardId, new { title = "Removed card" }))
            .GetProperty("id").GetGuid();
        using (var deleted = await SendAsync(AdminToken, HttpMethod.Delete, $"/api/tasks/{deletedId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        var originalBoard = (await GetBoardAsync(boardId)).GetRawText();
        var otherBoard = (await GetBoardAsync(otherBoardId)).GetRawText();
        (object Request, HttpStatusCode ExpectedStatus)[] invalidRequests =
        [
            (new { status = "Done", placement = "before" }, HttpStatusCode.BadRequest),
            (new { status = "Done", placement = "before", beforeTaskId = taskId }, HttpStatusCode.BadRequest),
            (new { status = "Done", placement = "top", beforeTaskId = wrongStateId }, HttpStatusCode.BadRequest),
            (new { status = "Done", placement = "before", beforeTaskId = wrongStateId }, HttpStatusCode.Conflict),
            (new { status = "Todo", placement = "before", beforeTaskId = otherBoardTaskId }, HttpStatusCode.Conflict),
            (new { status = "Todo", placement = "before", beforeTaskId = deletedId }, HttpStatusCode.Conflict),
            (new { status = "Done", placement = "before", beforeTaskId = Guid.NewGuid() }, HttpStatusCode.Conflict),
        ];

        foreach (var (request, expectedStatus) in invalidRequests)
        {
            using var response = await SendAsync(AdminToken, HttpMethod.Put, $"/api/tasks/{taskId}/status", request);
            Assert.Equal(expectedStatus, response.StatusCode);
            Assert.Equal(originalBoard, (await GetBoardAsync(boardId)).GetRawText());
            Assert.Equal(otherBoard, (await GetBoardAsync(otherBoardId)).GetRawText());
        }
    }

    [Fact]
    public async Task Invalid_task_placement_leaves_status_and_order_unchanged()
    {
        var workspaceId = await CreateWorkspaceAsync("Invalid placement");
        var boardId = (await CreateBoardAsync(workspaceId, "Validation"))
            .GetProperty("id").GetGuid();
        var task = await CreateTaskAsync(boardId, new { title = "Stay here" });
        var taskId = task.GetProperty("id").GetGuid();

        using var invalid = await SendAsync(
            AdminToken, HttpMethod.Put, $"/api/tasks/{taskId}/status", new { status = "Done", placement = "middle" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var persisted = (await GetBoardAsync(boardId)).GetProperty("tasks").EnumerateArray().Single();
        Assert.Equal("Todo", persisted.GetProperty("status").GetString());
        Assert.Equal(task.GetProperty("position").GetInt32(), persisted.GetProperty("position").GetInt32());
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
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal($"/api/tasks/{created.GetProperty("id").GetGuid()}", response.Headers.Location?.ToString());
        return created;
    }

    private async Task<JsonElement> GetBoardAsync(Guid boardId)
    {
        using var response = await SendAsync(AdminToken, HttpMethod.Get, $"/api/boards/{boardId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> GetStatsAsync(Guid boardId)
    {
        using var response = await SendAsync(
            AdminToken,
            HttpMethod.Get,
            $"/api/boards/{boardId}/stats");
        response.EnsureSuccessStatusCode();
        Assert.Equal("OFF", response.Headers.GetValues("X-Cache").Single());
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
