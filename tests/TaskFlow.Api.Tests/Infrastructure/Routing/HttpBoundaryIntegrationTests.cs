using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TaskFlow.Api.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Infrastructure.Routing;

[Collection(TaskFlowApiCollection.Name)]
public sealed class HttpBoundaryIntegrationTests(
    TaskFlowTestEnvironment environment,
    ITestOutputHelper output)
    : IntegrationTestBase(environment, output, authenticateAsAdmin: true)
{
    [Theory]
    [InlineData("{")]
    [InlineData("{\"username\":\"admin\"")]
    [InlineData("{\"username\":\"admin\",\"password\":")]
    public async Task Anonymous_JSON_endpoints_reject_malformed_or_truncated_documents(string json)
    {
        using var anonymous = CreateAnonymousClient();
        using var response = await SendRawAsync(
            anonymous,
            HttpMethod.Post,
            "/auth/login",
            json,
            "application/json");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("text/plain")]
    [InlineData("application/octet-stream")]
    public async Task JSON_endpoints_reject_non_JSON_or_absent_content_types(string? contentType)
    {
        using var anonymous = CreateAnonymousClient();
        const string validBody =
            "{\"username\":\"admin\",\"password\":\"test-admin-password\"}";
        using var response = await SendRawAsync(
            anonymous,
            HttpMethod.Post,
            "/auth/login",
            validBody,
            contentType);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/boards/b1f093e2-6f85-4ad8-9e2a-38d728416065/tasks", "{", "application/json")]
    [InlineData("POST", "/api/workspaces", "{\"name\":", "application/json")]
    [InlineData("PUT", "/api/tasks/b1f093e2-6f85-4ad8-9e2a-38d728416065/status", "{\"status\":", "application/json")]
    public async Task Protected_endpoints_authorize_before_attempting_body_binding(
        string method,
        string path,
        string body,
        string contentType)
    {
        using var anonymous = CreateAnonymousClient();
        using var response = await SendRawAsync(
            anonymous,
            new HttpMethod(method),
            path,
            body,
            contentType);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_requests_reach_body_binding_errors_without_mutating_state()
    {
        using var malformed = await SendRawAsync(
            Client,
            HttpMethod.Post,
            "/api/workspaces",
            "{\"name\":",
            "application/json");
        using var absentContentType = await SendRawAsync(
            Client,
            HttpMethod.Post,
            "/api/workspaces",
            "{\"name\":\"Must not exist\"}",
            contentType: null);

        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, absentContentType.StatusCode);

        using var workspaces = await Client.GetAsync("/api/workspaces");
        var body = await workspaces.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(
            body.EnumerateArray(),
            workspace => workspace.GetProperty("name").GetString() == "Must not exist");
    }

    [Theory]
    [InlineData("{\"title\":\"No change\",\"description\":\"\",\"priority\":1}")]
    [InlineData("{\"title\":\"No change\",\"description\":\"\",\"priority\":\"Legendary\"}")]
    public async Task Numeric_and_unknown_enum_values_are_rejected_during_JSON_binding(string json)
    {
        using var response = await SendRawAsync(
            Client,
            HttpMethod.Put,
            "/api/tasks/b1f093e2-6f85-4ad8-9e2a-38d728416065",
            json,
            "application/json");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/auth/login", "POST")]
    [InlineData("PATCH", "/api/boards/b1f093e2-6f85-4ad8-9e2a-38d728416065", "GET")]
    [InlineData("PATCH", "/api/boards/b1f093e2-6f85-4ad8-9e2a-38d728416065", "PUT")]
    [InlineData("GET", "/api/tasks/b1f093e2-6f85-4ad8-9e2a-38d728416065", "DELETE")]
    public async Task Known_routes_with_the_wrong_method_return_405_and_advertise_allowed_methods(
        string method,
        string path,
        string expectedAllowedMethod)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Contains(
            expectedAllowedMethod,
            response.Content.Headers.Allow,
            StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("GET", "/api/not-a-real-resource")]
    [InlineData("DELETE", "/api/not-a-real-resource")]
    [InlineData("GET", "/auth/not-a-real-resource")]
    [InlineData("POST", "/auth/not-a-real-resource")]
    [InlineData("POST", "/auth")]
    [InlineData("PUT", "/auth/")]
    [InlineData("GET", "/health/not-a-real-resource")]
    public async Task Unknown_API_and_auth_paths_return_a_JSON_404_instead_of_the_SPA(
        string method,
        string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Endpoint not found.", body.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("/api/boards/1")]
    [InlineData("/api/boards/-1")]
    [InlineData("/api/boards/2147483648")]
    [InlineData("/api/boards/999999999999999999999999")]
    [InlineData("/api/boards/not-a-uuid")]
    [InlineData("/api/boards/00000000-0000-0000-0000-000000000000")]
    [InlineData("/api/workspaces/1/boards")]
    [InlineData("/api/workspaces/1/members")]
    public async Task Integer_malformed_and_empty_UUID_route_values_are_concealed_as_404(string path)
    {
        using var response = await Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("DELETE", "/api/tasks/1")]
    [InlineData("PUT", "/api/tasks/1/status")]
    [InlineData("DELETE", "/api/workspaces/b1f093e2-6f85-4ad8-9e2a-38d728416065/members/1")]
    public async Task Integer_identifiers_cannot_address_mutation_endpoints(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { status = "Done" }),
        };
        using var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("\"1\"")]
    public async Task Integer_assignee_identifiers_are_rejected_without_mutating_the_task(string assigneeId)
    {
        var owner = await Application.CreateUserScenarioAsync("binding", createPersonalWorkspace: true);
        var boardId = owner.BoardId!.Value;
        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", owner.Token);
        using var created = await Client.PostAsJsonAsync(
            $"/api/boards/{boardId}/tasks", new { title = "Unchanged task" });
        created.EnsureSuccessStatusCode();
        var taskId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var json = $$"""
            {"title":"Must not persist","description":"","priority":"Medium","assigneeId":{{assigneeId}}}
            """;
        using var response = await SendRawAsync(
            Client,
            HttpMethod.Put,
            $"/api/tasks/{taskId}",
            json,
            "application/json");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var board = await Client.GetFromJsonAsync<JsonElement>($"/api/boards/{boardId}");
        var task = Assert.Single(board.GetProperty("tasks").EnumerateArray(),
            candidate => candidate.GetProperty("id").GetGuid() == taskId);
        Assert.Equal("Unchanged task", task.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, task.GetProperty("assigneeId").ValueKind);
    }

    [Theory]
    [InlineData("/auth")]
    [InlineData("/workspace/acme/boards/42?filter=mine")]
    [InlineData("/settings/profile/security")]
    [InlineData("/future/client/route")]
    [InlineData("/users/jane.doe")]
    public async Task Browser_deep_links_still_fall_back_to_the_SPA(string path)
    {
        using var anonymous = CreateAnonymousClient();
        using var response = await anonymous.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<div id=\"root\">", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/assets/missing.js")]
    [InlineData("/favicon.svg")]
    [InlineData("/missing.webmanifest")]
    public async Task Missing_static_assets_are_not_rewritten_to_the_SPA(string path)
    {
        using var anonymous = CreateAnonymousClient();
        using var response = await anonymous.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    private HttpClient CreateAnonymousClient() =>
        Application.CreateIntegrationClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    private static async Task<HttpResponseMessage> SendRawAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string body,
        string? contentType)
    {
        using var request = new HttpRequestMessage(method, path);
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        if (contentType is not null)
        {
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType)
            {
                CharSet = "utf-8",
            };
        }

        request.Content = content;
        return await client.SendAsync(request);
    }
}
