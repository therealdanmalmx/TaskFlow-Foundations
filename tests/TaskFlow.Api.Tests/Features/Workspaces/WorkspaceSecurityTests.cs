using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TaskFlow.Api.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Features.Workspaces;

[Collection(TaskFlowApiCollection.Name)]
public class WorkspaceSecurityTests(
    TaskFlowTestEnvironment environment,
    ITestOutputHelper output) : IntegrationTestBase(environment, output)
{
    private static int _forwardedAddressSequence;

    [Fact]
    public async Task Workspace_creation_trims_names_generates_unique_slugs_and_starts_empty()
    {
        var adminToken = await Application.CreateAdminTokenAsync();
        var workspaceName = $"Platform & Delivery {Guid.NewGuid():N}";

        using var firstResponse = await SendAsync(
            adminToken,
            HttpMethod.Post,
            "/api/workspaces",
            new { name = $"  {workspaceName}  " });
        using var secondResponse = await SendAsync(
            adminToken,
            HttpMethod.Post,
            "/api/workspaces",
            new { name = workspaceName });
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        var second = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(workspaceName, first.GetProperty("name").GetString());
        Assert.Equal("Owner", first.GetProperty("role").GetString());
        Assert.Equal(
            $"{first.GetProperty("slug").GetString()}-2",
            second.GetProperty("slug").GetString());

        var firstWorkspaceId = first.GetProperty("id").GetGuid();
        var boards = await GetJsonAsync(
            adminToken,
            $"/api/workspaces/{firstWorkspaceId}/boards");
        Assert.Empty(boards.EnumerateArray());

        using var blankName = await SendAsync(
            adminToken,
            HttpMethod.Post,
            "/api/workspaces",
            new { name = " " });
        using var longName = await SendAsync(
            adminToken,
            HttpMethod.Post,
            "/api/workspaces",
            new { name = new string('a', 101) });
        Assert.Equal(HttpStatusCode.BadRequest, blankName.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, longName.StatusCode);
    }

    [Fact]
    public async Task Concurrent_same_name_workspaces_receive_unique_slugs()
    {
        var adminToken = await Application.CreateAdminTokenAsync();
        var name = $"Workspace Race {Guid.NewGuid():N}";
        var responses = await Task.WhenAll(
            SendAsync(adminToken, HttpMethod.Post, "/api/workspaces", new { name }),
            SendAsync(adminToken, HttpMethod.Post, "/api/workspaces", new { name }));

        try
        {
            Assert.All(
                responses,
                response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
            var first = await responses[0].Content.ReadFromJsonAsync<JsonElement>();
            var second = await responses[1].Content.ReadFromJsonAsync<JsonElement>();
            Assert.NotEqual(
                first.GetProperty("slug").GetString(),
                second.GetProperty("slug").GetString());
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Registration_and_workspace_creation_share_slug_allocation_serialization()
    {
        var adminToken = await Application.CreateAdminTokenAsync();
        var slugBase = $"shared-slug-{Guid.NewGuid():N}";
        var registrationTask = PostWithForwardedAddressAsync(
            Client,
            "/auth/register",
            new
            {
                username = slugBase,
                email = $"{slugBase}@example.test",
                displayName = "Shared Slug Registration",
                password = "registration-password-123",
            });
        var workspaceTask = SendAsync(
            adminToken,
            HttpMethod.Post,
            "/api/workspaces",
            new { name = slugBase });
        var responses = await Task.WhenAll(registrationTask, workspaceTask);

        try
        {
            Assert.Equal(HttpStatusCode.Created, responses[0].StatusCode);
            Assert.Equal(HttpStatusCode.Created, responses[1].StatusCode);
            var session = await responses[0].Content.ReadFromJsonAsync<JsonElement>();
            var registrationToken = session.GetProperty("token").GetString()!;
            var personalWorkspace = Assert.Single(
                (await GetJsonAsync(registrationToken, "/api/workspaces")).EnumerateArray());
            var createdWorkspace = await responses[1].Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(
                new[] { slugBase, $"{slugBase}-2" },
                new[]
                {
                    personalWorkspace.GetProperty("slug").GetString()!,
                    createdWorkspace.GetProperty("slug").GetString()!,
                }.Order());
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Concurrent_duplicate_member_adds_return_created_and_conflict()
    {
        var ownerToken = await Application.CreateAdminTokenAsync();
        var member = await Application.CreateUserScenarioAsync("membership-race");
        using var workspaceResponse = await SendAsync(
            ownerToken,
            HttpMethod.Post,
            "/api/workspaces",
            new { name = $"Membership Race {Guid.NewGuid():N}" });
        workspaceResponse.EnsureSuccessStatusCode();
        var workspaceId = (await workspaceResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();
        var responses = await Task.WhenAll(
            SendAsync(
                ownerToken,
                HttpMethod.Post,
                $"/api/workspaces/{workspaceId}/members",
                new { username = member.Username, role = "Member" }),
            SendAsync(
                ownerToken,
                HttpMethod.Post,
                $"/api/workspaces/{workspaceId}/members",
                new { username = member.Username, role = "Member" }));

        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Membership_authorization_conceals_tenants_and_supports_normalized_invites_and_self_removal()
    {
        var ownerToken = await Application.CreateAdminTokenAsync();
        var manager = await Application.CreateUserScenarioAsync("manager");
        var member = await Application.CreateUserScenarioAsync("member");
        var outsider = await Application.CreateUserScenarioAsync("outsider");
        using var workspaceResponse = await SendAsync(
            ownerToken,
            HttpMethod.Post,
            "/api/workspaces",
            new { name = $"Authorization {Guid.NewGuid():N}" });
        workspaceResponse.EnsureSuccessStatusCode();
        var workspaceId = (await workspaceResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        using var addManager = await SendAsync(
            ownerToken,
            HttpMethod.Post,
            $"/api/workspaces/{workspaceId}/members",
            new { username = $"  {manager.Username.ToUpperInvariant()}  ", role = "Admin" });
        using var addMember = await SendAsync(
            ownerToken,
            HttpMethod.Post,
            $"/api/workspaces/{workspaceId}/members",
            new { username = member.Username, role = "Member" });
        Assert.Equal(HttpStatusCode.Created, addManager.StatusCode);
        Assert.Equal(HttpStatusCode.Created, addMember.StatusCode);

        foreach (var outsiderRequest in new[]
                 {
                     (HttpMethod.Get, $"/api/workspaces/{workspaceId}/members", (object?)null),
                     (HttpMethod.Get, $"/api/workspaces/{workspaceId}/boards", (object?)null),
                     (HttpMethod.Post, $"/api/workspaces/{workspaceId}/members", (object?)new { username = outsider.Username, role = "Member" }),
                     (HttpMethod.Put, $"/api/workspaces/{workspaceId}/members/{member.UserId}/role", (object?)new { role = "Admin" }),
                     (HttpMethod.Delete, $"/api/workspaces/{workspaceId}/members/{member.UserId}", (object?)null),
                 })
        {
            using var concealed = await SendAsync(
                outsider.Token,
                outsiderRequest.Item1,
                outsiderRequest.Item2,
                outsiderRequest.Item3);
            Assert.Equal(HttpStatusCode.NotFound, concealed.StatusCode);
        }

        using var memberCannotInvite = await SendAsync(
            member.Token,
            HttpMethod.Post,
            $"/api/workspaces/{workspaceId}/members",
            new { username = outsider.Username, role = "Member" });
        using var memberCannotRemoveManager = await SendAsync(
            member.Token,
            HttpMethod.Delete,
            $"/api/workspaces/{workspaceId}/members/{manager.UserId}");
        Assert.Equal(HttpStatusCode.Forbidden, memberCannotInvite.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, memberCannotRemoveManager.StatusCode);

        using var managerCannotCreateOwner = await SendAsync(
            manager.Token,
            HttpMethod.Post,
            $"/api/workspaces/{workspaceId}/members",
            new { username = outsider.Username, role = "Owner" });
        Assert.Equal(HttpStatusCode.Forbidden, managerCannotCreateOwner.StatusCode);

        using var managerAddsOutsider = await SendAsync(
            manager.Token,
            HttpMethod.Post,
            $"/api/workspaces/{workspaceId}/members",
            new { username = outsider.Username, role = "Member" });
        Assert.Equal(HttpStatusCode.Created, managerAddsOutsider.StatusCode);

        using var duplicateMembership = await SendAsync(
            ownerToken,
            HttpMethod.Post,
            $"/api/workspaces/{workspaceId}/members",
            new { username = outsider.Username, role = "Member" });
        using var unknownMember = await SendAsync(
            ownerToken,
            HttpMethod.Post,
            $"/api/workspaces/{workspaceId}/members",
            new { username = $"unknown-{Guid.NewGuid():N}", role = "Member" });
        using var invalidRole = await SendAsync(
            ownerToken,
            HttpMethod.Post,
            $"/api/workspaces/{workspaceId}/members",
            new { username = outsider.Username, role = "SuperAdmin" });
        Assert.Equal(HttpStatusCode.Conflict, duplicateMembership.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownMember.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidRole.StatusCode);

        var currentMembers = await GetJsonAsync(
            ownerToken,
            $"/api/workspaces/{workspaceId}/members");
        var ownerUserId = currentMembers.EnumerateArray()
            .Single(candidate =>
                candidate.GetProperty("username").GetString() == TaskFlowApplicationFactory.AdminUsername)
            .GetProperty("userId").GetGuid();
        using var managerCannotDemoteOwner = await SendAsync(
            manager.Token,
            HttpMethod.Put,
            $"/api/workspaces/{workspaceId}/members/{ownerUserId}/role",
            new { role = "Member" });
        Assert.Equal(HttpStatusCode.Forbidden, managerCannotDemoteOwner.StatusCode);

        using var memberLeaves = await SendAsync(
            member.Token,
            HttpMethod.Delete,
            $"/api/workspaces/{workspaceId}/members/{member.UserId}");
        Assert.Equal(HttpStatusCode.NoContent, memberLeaves.StatusCode);
        using var membersAfterLeaving = await SendAsync(
            member.Token,
            HttpMethod.Get,
            $"/api/workspaces/{workspaceId}/members");
        using var boardsAfterLeaving = await SendAsync(
            member.Token,
            HttpMethod.Get,
            $"/api/workspaces/{workspaceId}/boards");
        Assert.Equal(HttpStatusCode.NotFound, membersAfterLeaving.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, boardsAfterLeaving.StatusCode);

        var memberWorkspaces = await GetJsonAsync(member.Token, "/api/workspaces");
        Assert.DoesNotContain(
            memberWorkspaces.EnumerateArray(),
            candidate => candidate.GetProperty("id").GetGuid() == workspaceId);
    }

    private async Task<JsonElement> GetJsonAsync(string token, string path)
    {
        using var response = await SendAsync(token, HttpMethod.Get, path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HttpResponseMessage> SendAsync(
        string token,
        HttpMethod method,
        string path,
        object? body = null,
        HttpClient? client = null,
        string? forwardedAddress = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (forwardedAddress is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedAddress);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await (client ?? Client).SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostWithForwardedAddressAsync(
        HttpClient client,
        string path,
        object body,
        string? forwardedAddress = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-Forwarded-For", forwardedAddress ?? NextForwardedAddress());
        return await client.SendAsync(request);
    }

    private static string NextForwardedAddress()
    {
        var value = Interlocked.Increment(ref _forwardedAddressSequence);
        return $"198.19.{value / 250}.{value % 250 + 1}";
    }
}
