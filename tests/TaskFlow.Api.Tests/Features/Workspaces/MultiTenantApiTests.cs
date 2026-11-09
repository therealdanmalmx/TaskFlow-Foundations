using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TaskFlow.Api.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Features.Workspaces;

[Collection(TaskFlowApiCollection.Name)]
public class MultiTenantApiTests(TaskFlowTestEnvironment environment, ITestOutputHelper output)
    : IntegrationTestBase(environment, output)
{
    private int _clientIp = 10;

    [Fact]
    public async Task Registration_creates_a_personal_tenant_and_tenant_ids_are_isolated()
    {
        var alice = await RegisterAsync("alice");
        var bob = await RegisterAsync("bob");

        var aliceWorkspace = await GetFirstAsync(alice.Token, "/api/workspaces");
        var bobWorkspace = await GetFirstAsync(bob.Token, "/api/workspaces");
        var aliceWorkspaceId = aliceWorkspace.GetProperty("id").GetGuid();
        var bobWorkspaceId = bobWorkspace.GetProperty("id").GetGuid();
        Assert.NotEqual(aliceWorkspaceId, bobWorkspaceId);
        Assert.Equal("Owner", aliceWorkspace.GetProperty("role").GetString());

        var aliceBoard = await GetFirstAsync(
            alice.Token,
            $"/api/workspaces/{aliceWorkspaceId}/boards");
        var bobBoard = await GetFirstAsync(
            bob.Token,
            $"/api/workspaces/{bobWorkspaceId}/boards");
        var aliceBoardId = aliceBoard.GetProperty("id").GetGuid();
        var bobBoardId = bobBoard.GetProperty("id").GetGuid();
        var originalAliceBoardUpdatedAt = aliceBoard.GetProperty("updatedAt").GetDateTimeOffset();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await SendAsync(alice.Token, HttpMethod.Get, $"/api/boards/{bobBoardId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await SendAsync(bob.Token, HttpMethod.Get, $"/api/boards/{aliceBoardId}")).StatusCode);

        var bobTask = await SendAsync(
            bob.Token,
            HttpMethod.Post,
            $"/api/boards/{bobBoardId}/tasks",
            new { title = "Private Bob work", description = "Bob only", priority = "Low" });
        Assert.Equal(HttpStatusCode.Created, bobTask.StatusCode);
        var bobTaskId = (await bobTask.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();
        Guid[] identifiers =
            [alice.UserId, bob.UserId, aliceWorkspaceId, bobWorkspaceId, aliceBoardId, bobBoardId, bobTaskId];
        Assert.Equal(identifiers.Length, identifiers.Distinct().Count());
        Assert.All(identifiers, id => Assert.Equal('4', id.ToString("D")[14]));
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await SendAsync(
                alice.Token,
                HttpMethod.Put,
                $"/api/tasks/{bobTaskId}",
                new
                {
                    title = "Guessed update",
                    description = "Must stay isolated",
                    priority = "Urgent",
                    dueDate = "2030-01-03",
                    assigneeId = (Guid?)null,
                })).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await SendAsync(
                alice.Token,
                HttpMethod.Put,
                $"/api/tasks/{bobTaskId}/status",
                new { status = "Done" })).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await SendAsync(alice.Token, HttpMethod.Delete, $"/api/tasks/{bobTaskId}")).StatusCode);

        var invalidAssignment = await SendAsync(
            alice.Token,
            HttpMethod.Post,
            $"/api/boards/{aliceBoardId}/tasks",
            new
            {
                title = "Tenant boundary",
                description = "Bob is not a member yet",
                priority = "High",
                dueDate = "2030-01-02",
                assigneeId = bob.UserId,
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidAssignment.StatusCode);

        var added = await SendAsync(
            alice.Token,
            HttpMethod.Post,
            $"/api/workspaces/{aliceWorkspaceId}/members",
            new { username = bob.Username, role = "Member" });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        var created = await SendAsync(
            alice.Token,
            HttpMethod.Post,
            $"/api/boards/{aliceBoardId}/tasks",
            new
            {
                title = "Ship tenant support",
                description = "Exercise rich fields",
                priority = "High",
                dueDate = "2030-01-02",
                assigneeId = bob.UserId,
            });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var task = await created.Content.ReadFromJsonAsync<JsonElement>();
        var taskId = task.GetProperty("id").GetGuid();
        Assert.Equal("High", task.GetProperty("priority").GetString());
        Assert.Equal("2030-01-02", task.GetProperty("dueDate").GetString());
        Assert.Equal(bob.UserId, task.GetProperty("assigneeId").GetGuid());
        Assert.Equal(bob.DisplayName, task.GetProperty("assigneeName").GetString());

        var boardAfterCreate = await GetAsync(alice.Token, $"/api/boards/{aliceBoardId}");
        var afterCreateUpdatedAt = boardAfterCreate.GetProperty("updatedAt").GetDateTimeOffset();
        Assert.True(afterCreateUpdatedAt > originalAliceBoardUpdatedAt);

        await Task.Delay(5);
        var updated = await SendAsync(
            bob.Token,
            HttpMethod.Put,
            $"/api/tasks/{taskId}",
            new
            {
                title = "Ship isolated tenant support",
                description = "Updated by a workspace member",
                priority = "Urgent",
                dueDate = "2030-01-03",
                assigneeId = bob.UserId,
            });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedTask = await updated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Urgent", updatedTask.GetProperty("priority").GetString());
        Assert.Equal("Updated by a workspace member", updatedTask.GetProperty("description").GetString());

        var boardAfterUpdate = await GetAsync(alice.Token, $"/api/boards/{aliceBoardId}");
        var afterUpdateUpdatedAt = boardAfterUpdate.GetProperty("updatedAt").GetDateTimeOffset();
        Assert.True(afterUpdateUpdatedAt > afterCreateUpdatedAt);

        await Task.Delay(5);
        Assert.Equal(
            HttpStatusCode.OK,
            (await SendAsync(
                bob.Token,
                HttpMethod.Put,
                $"/api/tasks/{taskId}/status",
                new { status = "Doing" })).StatusCode);
        var boardAfterStatus = await GetAsync(alice.Token, $"/api/boards/{aliceBoardId}");
        var afterStatusUpdatedAt = boardAfterStatus.GetProperty("updatedAt").GetDateTimeOffset();
        Assert.True(afterStatusUpdatedAt > afterUpdateUpdatedAt);

        await Task.Delay(5);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await SendAsync(bob.Token, HttpMethod.Delete, $"/api/tasks/{taskId}")).StatusCode);
        var boardAfterDelete = await GetAsync(alice.Token, $"/api/boards/{aliceBoardId}");
        Assert.True(boardAfterDelete.GetProperty("updatedAt").GetDateTimeOffset() > afterStatusUpdatedAt);
    }

    [Fact]
    public async Task Roles_protect_member_and_board_management_and_archived_boards_are_read_only()
    {
        var owner = await Application.CreateUserScenarioAsync("owner", createPersonalWorkspace: true);
        var admin = await Application.CreateUserScenarioAsync("admin");
        var member = await Application.CreateUserScenarioAsync("member");
        var removable = await Application.CreateUserScenarioAsync("removable");
        var workspace = await GetFirstAsync(owner.Token, "/api/workspaces");
        var workspaceId = workspace.GetProperty("id").GetGuid();

        Assert.Equal(
            HttpStatusCode.Created,
            (await SendAsync(
                owner.Token,
                HttpMethod.Post,
                $"/api/workspaces/{workspaceId}/members",
                new { username = admin.Username, role = "Admin" })).StatusCode);
        Assert.Equal(
            HttpStatusCode.Created,
            (await SendAsync(
                owner.Token,
                HttpMethod.Post,
                $"/api/workspaces/{workspaceId}/members",
                new { username = member.Username, role = "Member" })).StatusCode);
        Assert.Equal(
            HttpStatusCode.Created,
            (await SendAsync(
                owner.Token,
                HttpMethod.Post,
                $"/api/workspaces/{workspaceId}/members",
                new { username = removable.Username, role = "Member" })).StatusCode);

        var createdBoard = await SendAsync(
            admin.Token,
            HttpMethod.Post,
            $"/api/workspaces/{workspaceId}/boards",
            new { name = "Release", description = "Release work", color = "#7c3aed" });
        Assert.Equal(HttpStatusCode.Created, createdBoard.StatusCode);
        var boardId = (await createdBoard.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SendAsync(
                member.Token,
                HttpMethod.Post,
                $"/api/workspaces/{workspaceId}/boards",
                new { name = "Forbidden", description = "", color = "#2563eb" })).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SendAsync(
                admin.Token,
                HttpMethod.Put,
                $"/api/workspaces/{workspaceId}/members/{owner.UserId}/role",
                new { role = "Member" })).StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await SendAsync(
                owner.Token,
                HttpMethod.Put,
                $"/api/workspaces/{workspaceId}/members/{owner.UserId}/role",
                new { role = "Member" })).StatusCode);

        var memberTask = await SendAsync(
            member.Token,
            HttpMethod.Post,
            $"/api/boards/{boardId}/tasks",
            new { title = "Member work", description = "", priority = "Medium" });
        Assert.Equal(HttpStatusCode.Created, memberTask.StatusCode);
        var memberTaskId = (await memberTask.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var assignmentStatusAndRemoval = await Task.WhenAll(
            SendAsync(
                member.Token,
                HttpMethod.Put,
                $"/api/tasks/{memberTaskId}",
                new
                {
                    title = "Member work",
                    description = "Racing an assignee removal",
                    priority = "Medium",
                    dueDate = (string?)null,
                    assigneeId = removable.UserId,
                }),
            SendAsync(
                removable.Token,
                HttpMethod.Put,
                $"/api/tasks/{memberTaskId}/status",
                new { status = "Doing" }),
            SendAsync(
                owner.Token,
                HttpMethod.Delete,
                $"/api/workspaces/{workspaceId}/members/{removable.UserId}"));
        Assert.Contains(
            assignmentStatusAndRemoval[0].StatusCode,
            new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest });
        Assert.Contains(
            assignmentStatusAndRemoval[1].StatusCode,
            new[] { HttpStatusCode.OK, HttpStatusCode.NotFound });
        Assert.Equal(HttpStatusCode.NoContent, assignmentStatusAndRemoval[2].StatusCode);

        var boardAfterConcurrentRemoval = await GetAsync(owner.Token, $"/api/boards/{boardId}");
        var taskAfterConcurrentRemoval = boardAfterConcurrentRemoval.GetProperty("tasks").EnumerateArray()
            .Single(task => task.GetProperty("id").GetGuid() == memberTaskId);
        Assert.Equal(JsonValueKind.Null, taskAfterConcurrentRemoval.GetProperty("assigneeId").ValueKind);

        Assert.Equal(
            HttpStatusCode.OK,
            (await SendAsync(
                member.Token,
                HttpMethod.Put,
                $"/api/tasks/{memberTaskId}",
                new
                {
                    title = "Member work",
                    description = "Assigned to the admin",
                    priority = "Medium",
                    dueDate = (string?)null,
                    assigneeId = admin.UserId,
                })).StatusCode);

        var updatedBoard = await SendAsync(
            admin.Token,
            HttpMethod.Put,
            $"/api/boards/{boardId}",
            new { name = "Release train", description = "Updated", color = "#059669" });
        Assert.Equal(HttpStatusCode.OK, updatedBoard.StatusCode);
        var updatedBoardBody = await updatedBoard.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Admin", updatedBoardBody.GetProperty("role").GetString());
        Assert.Contains(
            updatedBoardBody.GetProperty("tasks").EnumerateArray(),
            task => task.GetProperty("title").GetString() == "Member work");

        var raceBoard = await SendAsync(
            admin.Token,
            HttpMethod.Post,
            $"/api/workspaces/{workspaceId}/boards",
            new { name = "Archive race", description = "Lock invariant", color = "#2563eb" });
        Assert.Equal(HttpStatusCode.Created, raceBoard.StatusCode);
        var raceBoardId = (await raceBoard.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();
        var archiveAndCreate = await Task.WhenAll(
            SendAsync(
                admin.Token,
                HttpMethod.Put,
                $"/api/boards/{raceBoardId}/archive",
                new { isArchived = true }),
            SendAsync(
                member.Token,
                HttpMethod.Post,
                $"/api/boards/{raceBoardId}/tasks",
                new { title = "Race the archive", description = "", priority = "Medium" }));
        Assert.Equal(HttpStatusCode.OK, archiveAndCreate[0].StatusCode);
        Assert.Contains(
            archiveAndCreate[1].StatusCode,
            new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });
        var raceBoardAfter = await GetAsync(owner.Token, $"/api/boards/{raceBoardId}");
        Assert.True(raceBoardAfter.GetProperty("isArchived").GetBoolean());
        if (archiveAndCreate[1].StatusCode == HttpStatusCode.Created)
        {
            var racedTask = raceBoardAfter.GetProperty("tasks").EnumerateArray().Single();
            Assert.True(
                racedTask.GetProperty("createdAt").GetDateTimeOffset() <=
                raceBoardAfter.GetProperty("updatedAt").GetDateTimeOffset());
        }
        else
        {
            Assert.Empty(raceBoardAfter.GetProperty("tasks").EnumerateArray());
        }

        var archivedBoard = await SendAsync(
            admin.Token,
            HttpMethod.Put,
            $"/api/boards/{boardId}/archive",
            new { isArchived = true });
        Assert.Equal(HttpStatusCode.OK, archivedBoard.StatusCode);
        var archivedBoardBody = await archivedBoard.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Admin", archivedBoardBody.GetProperty("role").GetString());
        Assert.True(archivedBoardBody.GetProperty("isArchived").GetBoolean());
        Assert.Contains(
            archivedBoardBody.GetProperty("tasks").EnumerateArray(),
            task => task.GetProperty("title").GetString() == "Member work");
        var archivedAt = archivedBoardBody.GetProperty("updatedAt").GetDateTimeOffset();
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await SendAsync(
                member.Token,
                HttpMethod.Post,
                $"/api/boards/{boardId}/tasks",
                new { title = "Too late", description = "", priority = "Low" })).StatusCode);

        await Task.Delay(5);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await SendAsync(
                owner.Token,
                HttpMethod.Delete,
                $"/api/workspaces/{workspaceId}/members/{admin.UserId}")).StatusCode);
        var boardAfterMemberRemoval = await GetAsync(owner.Token, $"/api/boards/{boardId}");
        Assert.True(boardAfterMemberRemoval.GetProperty("updatedAt").GetDateTimeOffset() > archivedAt);
        var unassignedTask = boardAfterMemberRemoval.GetProperty("tasks").EnumerateArray()
            .Single(task => task.GetProperty("id").GetGuid() == memberTaskId);
        Assert.Equal(JsonValueKind.Null, unassignedTask.GetProperty("assigneeId").ValueKind);

        var coOwner = await Application.CreateUserScenarioAsync("co-owner");
        Assert.Equal(
            HttpStatusCode.Created,
            (await SendAsync(
                owner.Token,
                HttpMethod.Post,
                $"/api/workspaces/{workspaceId}/members",
                new { username = coOwner.Username, role = "Owner" })).StatusCode);

        var concurrentDemotions = await Task.WhenAll(
            SendAsync(
                owner.Token,
                HttpMethod.Put,
                $"/api/workspaces/{workspaceId}/members/{owner.UserId}/role",
                new { role = "Member" }),
            SendAsync(
                coOwner.Token,
                HttpMethod.Put,
                $"/api/workspaces/{workspaceId}/members/{coOwner.UserId}/role",
                new { role = "Member" }));
        Assert.Contains(concurrentDemotions, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Contains(concurrentDemotions, response => response.StatusCode == HttpStatusCode.Conflict);

        var membersAfterDemotion = await GetAsync(
            member.Token,
            $"/api/workspaces/{workspaceId}/members");
        var remainingOwnerId = membersAfterDemotion.EnumerateArray()
            .Single(candidate => candidate.GetProperty("role").GetString() == "Owner")
            .GetProperty("userId").GetGuid();
        var remainingOwnerToken = remainingOwnerId == owner.UserId ? owner.Token : coOwner.Token;
        var otherOwnerId = remainingOwnerId == owner.UserId ? coOwner.UserId : owner.UserId;
        Assert.Equal(
            HttpStatusCode.OK,
            (await SendAsync(
                remainingOwnerToken,
                HttpMethod.Put,
                $"/api/workspaces/{workspaceId}/members/{otherOwnerId}/role",
                new { role = "Owner" })).StatusCode);

        var concurrentRemovals = await Task.WhenAll(
            SendAsync(
                owner.Token,
                HttpMethod.Delete,
                $"/api/workspaces/{workspaceId}/members/{owner.UserId}"),
            SendAsync(
                coOwner.Token,
                HttpMethod.Delete,
                $"/api/workspaces/{workspaceId}/members/{coOwner.UserId}"));
        Assert.Contains(concurrentRemovals, response => response.StatusCode == HttpStatusCode.NoContent);
        Assert.Contains(concurrentRemovals, response => response.StatusCode == HttpStatusCode.Conflict);

        var membersAfterRemoval = await GetAsync(
            member.Token,
            $"/api/workspaces/{workspaceId}/members");
        Assert.Single(membersAfterRemoval.EnumerateArray()
, candidate => candidate.GetProperty("role").GetString() == "Owner");
    }

    private async Task<(string Token, Guid UserId, string Username, string DisplayName, string Password)> RegisterAsync(
        string prefix)
    {
        var unique = Guid.NewGuid().ToString("N")[..10];
        var username = $"{prefix}-{unique}";
        var displayName = $"{prefix} {unique}";
        var password = "registration-password-123";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/register")
        {
            Content = JsonContent.Create(new
            {
                username,
                email = $"{username}@example.test",
                displayName,
                password,
            }),
        };
        request.Headers.Add("X-Forwarded-For", $"198.51.100.{_clientIp++}");
        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (
            body.GetProperty("token").GetString()!,
            body.GetProperty("user").GetProperty("id").GetGuid(),
            username,
            displayName,
            password);
    }

    private async Task<JsonElement> GetFirstAsync(string token, string path)
    {
        var response = await SendAsync(token, HttpMethod.Get, path);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.EnumerateArray().First().Clone();
    }

    private async Task<JsonElement> GetAsync(string token, string path)
    {
        var response = await SendAsync(token, HttpMethod.Get, path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HttpResponseMessage> SendAsync(
        string token,
        HttpMethod method,
        string path,
        object? body = null)
    {
        return await Client.SendAuthenticatedAsync(token, method, path, body);
    }
}
