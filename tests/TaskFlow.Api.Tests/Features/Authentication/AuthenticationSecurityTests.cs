using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using TaskFlow.Api.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Features.Authentication;

[Collection(TaskFlowApiCollection.Name)]
public class AuthenticationSecurityTests(
    TaskFlowTestEnvironment environment,
    ITestOutputHelper output) : IntegrationTestBase(environment, output)
{
    private static int _forwardedAddressSequence;

    [Fact]
    public async Task Identity_and_workspace_routes_require_authentication()
    {
        var routes = new[]
        {
            (HttpMethod.Get, "/api/users/me"),
            (HttpMethod.Put, "/api/users/me"),
            (HttpMethod.Put, "/api/users/me/password"),
            (HttpMethod.Get, "/api/workspaces"),
            (HttpMethod.Post, "/api/workspaces"),
            (HttpMethod.Get, "/api/workspaces/b1f093e2-6f85-4ad8-9e2a-38d728416065/members"),
            (HttpMethod.Post, "/api/workspaces/b1f093e2-6f85-4ad8-9e2a-38d728416065/members"),
            (HttpMethod.Put, "/api/workspaces/b1f093e2-6f85-4ad8-9e2a-38d728416065/members/b1f093e2-6f85-4ad8-9e2a-38d728416065/role"),
            (HttpMethod.Delete, "/api/workspaces/b1f093e2-6f85-4ad8-9e2a-38d728416065/members/b1f093e2-6f85-4ad8-9e2a-38d728416065"),
            (HttpMethod.Post, "/auth/logout"),
        };

        foreach (var (method, path) in routes)
        {
            using var request = new HttpRequestMessage(method, path);
            if (method == HttpMethod.Post || method == HttpMethod.Put)
            {
                request.Content = JsonContent.Create(new { });
            }

            using var response = await Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Registration_normalizes_identity_and_atomically_creates_a_personal_tenant()
    {
        var unique = Guid.NewGuid().ToString("N")[..10];
        var username = $"Mixed-{unique}";
        var email = $"Mixed-{unique}@Example.Test";
        var displayName = $"Mixed Person {unique}";
        var password = "registration-password-123";

        using var registration = await PostWithForwardedAddressAsync(
            Client,
            "/auth/register",
            new
            {
                username = $"  {username}  ",
                email = $"  {email}  ",
                displayName = $"  {displayName}  ",
                password,
            });

        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        Assert.Equal("/api/users/me", registration.Headers.Location?.OriginalString);
        var session = await registration.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(username, session.GetProperty("username").GetString());
        Assert.Equal(email, session.GetProperty("user").GetProperty("email").GetString());
        Assert.Equal(displayName, session.GetProperty("user").GetProperty("displayName").GetString());
        var token = session.GetProperty("token").GetString()!;

        using var normalizedLogin = await PostWithForwardedAddressAsync(
            Client,
            "/auth/login",
            new { username = $"  {username.ToUpperInvariant()}  ", password });
        Assert.Equal(HttpStatusCode.OK, normalizedLogin.StatusCode);

        var workspaces = await GetJsonAsync(token, "/api/workspaces");
        var workspace = Assert.Single(workspaces.EnumerateArray());
        Assert.Equal("Owner", workspace.GetProperty("role").GetString());
        Assert.Equal($"{displayName}'s Workspace", workspace.GetProperty("name").GetString());
        var workspaceId = workspace.GetProperty("id").GetGuid();

        var members = await GetJsonAsync(token, $"/api/workspaces/{workspaceId}/members");
        var owner = Assert.Single(members.EnumerateArray());
        Assert.Equal(username, owner.GetProperty("username").GetString());
        Assert.Equal("Owner", owner.GetProperty("role").GetString());

        var boards = await GetJsonAsync(token, $"/api/workspaces/{workspaceId}/boards");
        var board = Assert.Single(boards.EnumerateArray());
        Assert.Equal("My tasks", board.GetProperty("name").GetString());

        using var duplicateUsername = await PostWithForwardedAddressAsync(
            Client,
            "/auth/register",
            new
            {
                username = username.ToLowerInvariant(),
                email = $"different-{unique}@example.test",
                displayName,
                password,
            });
        using var duplicateEmail = await PostWithForwardedAddressAsync(
            Client,
            "/auth/register",
            new
            {
                username = $"different-{unique}",
                email = email.ToLowerInvariant(),
                displayName,
                password,
            });

        Assert.Equal(HttpStatusCode.Conflict, duplicateUsername.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicateEmail.StatusCode);
        Assert.Equal(
            await ErrorAsync(duplicateUsername),
            await ErrorAsync(duplicateEmail));
    }

    [Fact]
    public async Task Concurrent_duplicate_registrations_create_exactly_one_identity()
    {
        var unique = Guid.NewGuid().ToString("N")[..10];
        var username = $"registration-race-{unique}";
        var email = $"registration-race-{unique}@example.test";
        var responses = await Task.WhenAll(
            PostWithForwardedAddressAsync(
                Client,
                "/auth/register",
                new
                {
                    username,
                    email,
                    displayName = "Registration Race One",
                    password = "registration-password-123",
                }),
            PostWithForwardedAddressAsync(
                Client,
                "/auth/register",
                new
                {
                    username = username.ToUpperInvariant(),
                    email = email.ToUpperInvariant(),
                    displayName = "Registration Race Two",
                    password = "registration-password-123",
                }));

        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            var created = responses.Single(response => response.StatusCode == HttpStatusCode.Created);
            var session = await created.Content.ReadFromJsonAsync<JsonElement>();
            var token = session.GetProperty("token").GetString()!;
            var workspaces = await GetJsonAsync(token, "/api/workspaces");
            Assert.Single(workspaces.EnumerateArray());
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
    public async Task Registration_rejects_invalid_identity_boundaries_with_specific_errors()
    {
        const string validUsername = "valid-user";
        const string validEmail = "valid-user@example.test";
        const string validDisplayName = "Valid User";
        const string validPassword = "registration-password-123";
        var cases = new (object Body, string Error)[]
        {
            (
                new { username = "ab", email = validEmail, displayName = validDisplayName, password = validPassword },
                "Username must be between 3 and 50 characters."),
            (
                new { username = new string('a', 51), email = validEmail, displayName = validDisplayName, password = validPassword },
                "Username must be between 3 and 50 characters."),
            (
                new { username = "invalid user", email = validEmail, displayName = validDisplayName, password = validPassword },
                "Username can contain only letters, numbers, dots, hyphens, and underscores."),
            (
                new { username = validUsername, email = "invalid@@example.test", displayName = validDisplayName, password = validPassword },
                "A valid email address is required."),
            (
                new { username = validUsername, email = $"{new string('a', 250)}@example.test", displayName = validDisplayName, password = validPassword },
                "A valid email address is required."),
            (
                new { username = validUsername, email = validEmail, displayName = new string('a', 101), password = validPassword },
                "Display name is required and must be 100 characters or fewer."),
            (
                new { username = validUsername, email = validEmail, displayName = validDisplayName, password = "too-short" },
                "Password must be between 12 and 200 characters."),
            (
                new { username = validUsername, email = validEmail, displayName = validDisplayName, password = new string('a', 201) },
                "Password must be between 12 and 200 characters."),
        };

        foreach (var testCase in cases)
        {
            using var response = await PostWithForwardedAddressAsync(
                Client,
                "/auth/register",
                testCase.Body);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(testCase.Error, await ErrorAsync(response));
        }
    }

    [Fact]
    public async Task Registration_and_login_limits_are_partitioned_by_effective_client_address()
    {
        var client = Client;
        const string registrationAddress = "198.18.10.10";

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var rejected = await PostWithForwardedAddressAsync(
                client,
                "/auth/register",
                new { },
                registrationAddress);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        using var registrationThrottled = await PostWithForwardedAddressAsync(
            client,
            "/auth/register",
            new { },
            registrationAddress);
        Assert.Equal(HttpStatusCode.TooManyRequests, registrationThrottled.StatusCode);

        using var differentRegistrationAddress = await PostWithForwardedAddressAsync(
            client,
            "/auth/register",
            new { },
            "198.18.10.11");
        Assert.Equal(HttpStatusCode.BadRequest, differentRegistrationAddress.StatusCode);

        const string loginAddress = "198.18.20.20";
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var rejected = await PostWithForwardedAddressAsync(
                client,
                "/auth/login",
                new { },
                loginAddress);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        using var loginThrottled = await PostWithForwardedAddressAsync(
            client,
            "/auth/login",
            new { },
            loginAddress);
        Assert.Equal(HttpStatusCode.TooManyRequests, loginThrottled.StatusCode);

        using var differentLoginAddress = await PostWithForwardedAddressAsync(
            client,
            "/auth/login",
            new
            {
                username = TaskFlowApplicationFactory.AdminUsername,
                password = TaskFlowApplicationFactory.AdminPassword,
            },
            "198.18.20.21");
        Assert.Equal(HttpStatusCode.OK, differentLoginAddress.StatusCode);
    }

    [Fact]
    public async Task Cookie_and_bearer_sessions_have_secure_precedence_and_global_revocation_semantics()
    {
        using var cookieClient = Application.CreateIntegrationClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });
        var unique = Guid.NewGuid().ToString("N")[..10];
        var username = $"session-{unique}";
        var password = "session-password-123";
        using var registered = await PostWithForwardedAddressAsync(
            cookieClient,
            "/auth/register",
            new
            {
                username,
                email = $"{username}@example.test",
                displayName = $"Session {unique}",
                password,
            });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var registrationSession = await registered.Content.ReadFromJsonAsync<JsonElement>();
        var registrationToken = registrationSession.GetProperty("token").GetString()!;
        var developmentCookie = SessionCookie(registered);
        Assert.True(HasCookieAttribute(developmentCookie, "httponly"));
        Assert.True(HasCookieAttribute(developmentCookie, "samesite=strict"));
        Assert.True(HasCookieAttribute(developmentCookie, "path=/"));
        Assert.False(HasCookieAttribute(developmentCookie, "secure"));
        using var profileBeforeInvalidBearer = await cookieClient.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.OK, profileBeforeInvalidBearer.StatusCode);

        using var invalidBearer = new HttpRequestMessage(HttpMethod.Get, "/api/users/me");
        invalidBearer.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");
        using var invalidBearerResponse = await cookieClient.SendAsync(invalidBearer);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidBearerResponse.StatusCode);
        using var profileAfterInvalidBearer = await cookieClient.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.OK, profileAfterInvalidBearer.StatusCode);

        using var secondLogin = await PostWithForwardedAddressAsync(
            Client,
            "/auth/login",
            new { username, password });
        Assert.Equal(HttpStatusCode.OK, secondLogin.StatusCode);
        var secondToken = (await secondLogin.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("token").GetString()!;

        using var logout = await cookieClient.PostAsync("/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains("expires=", SessionCookie(logout), StringComparison.OrdinalIgnoreCase);
        using var cookieAfterLogout = await cookieClient.GetAsync("/api/users/me");
        using var registrationTokenAfterLogout = await SendAsync(
            registrationToken,
            HttpMethod.Get,
            "/api/users/me");
        using var secondTokenAfterLogout = await SendAsync(
            secondToken,
            HttpMethod.Get,
            "/api/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, cookieAfterLogout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, registrationTokenAfterLogout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, secondTokenAfterLogout.StatusCode);

        using var productionFactory = Application.WithWebHostBuilder(builder =>
            builder.UseEnvironment("Production"));
        using var productionClient = productionFactory.CreateIntegrationClient();
        using var productionLogin = await PostWithForwardedAddressAsync(
            productionClient,
            "/auth/login",
            new { username, password });
        Assert.Equal(HttpStatusCode.OK, productionLogin.StatusCode);
        Assert.True(HasCookieAttribute(SessionCookie(productionLogin), "secure"));
        var replacementToken = (await productionLogin.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("token").GetString()!;
        using var replacementTokenProfile = await SendAsync(
            replacementToken,
            HttpMethod.Get,
            "/api/users/me");
        Assert.Equal(HttpStatusCode.OK, replacementTokenProfile.StatusCode);
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

    private static async Task<string> ErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("error").GetString()!;
    }

    private static string SessionCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("taskflow_session=", StringComparison.Ordinal));

    private static bool HasCookieAttribute(string cookie, string expected) =>
        cookie.Split(';')
            .Skip(1)
            .Select(attribute => attribute.Trim())
            .Contains(expected, StringComparer.OrdinalIgnoreCase);
}
