using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TaskFlow.Api.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Features.Authentication;

[Collection(TaskFlowApiCollection.Name)]
public class AuthApiTests(TaskFlowTestEnvironment environment, ITestOutputHelper output)
    : IntegrationTestBase(environment, output)
{
    [Fact]
    public async Task Logging_in_with_valid_credentials_returns_a_working_token()
    {
        var response = await Client.PostAsJsonAsync("/auth/login",
            new
            {
                username = TaskFlowApplicationFactory.AdminUsername,
                password = TaskFlowApplicationFactory.AdminPassword,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(TaskFlowApplicationFactory.AdminUsername, body.GetProperty("username").GetString());
        Assert.True(body.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);

        var token = body.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var profile = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
    }

    [Fact]
    public async Task Logging_in_with_a_wrong_password_returns_401()
    {
        var response = await Client.PostAsJsonAsync("/auth/login",
            new { username = TaskFlowApplicationFactory.AdminUsername, password = "wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logging_in_with_an_unknown_username_returns_the_same_401()
    {
        // Same status and message as a wrong password — no username oracle.
        var response = await Client.PostAsJsonAsync("/auth/login",
            new { username = "nobody", password = "irrelevant-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Invalid username or password.", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Logging_in_without_credentials_returns_400()
    {
        var response = await Client.PostAsJsonAsync("/auth/login", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/boards/b1f093e2-6f85-4ad8-9e2a-38d728416065")]
    [InlineData("GET", "/api/boards/b1f093e2-6f85-4ad8-9e2a-38d728416065/stats")]
    [InlineData("GET", "/api/boards/b1f093e2-6f85-4ad8-9e2a-38d728416065/activity")]
    [InlineData("POST", "/api/boards/b1f093e2-6f85-4ad8-9e2a-38d728416065/tasks")]
    [InlineData("DELETE", "/api/tasks/b1f093e2-6f85-4ad8-9e2a-38d728416065")]
    public async Task Requests_without_a_token_are_rejected_with_401(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { title = "no token" });
        }

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_tampered_token_is_rejected_with_401()
    {
        // Flip the end of the signature: the payload still parses, but HS256
        // verification must fail.
        var token = await Application.CreateAdminTokenAsync();
        var tampered = token[..^4] + (token.EndsWith("AAAA") ? "BBBB" : "AAAA");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tampered);
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_and_the_spa_stay_anonymous()
    {
        // Docker/Dokploy probe readiness with no credentials, and the SPA has
        // to load so the user can reach the login form in the first place.
        var ready = await Client.GetAsync("/health/ready");
        var spa = await Client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, spa.StatusCode);
    }

    [Fact]
    public async Task Login_is_rate_limited_after_ten_attempts()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var response = await Client.PostAsJsonAsync("/auth/login",
                new { username = TaskFlowApplicationFactory.AdminUsername, password = "wrong-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var throttled = await Client.PostAsJsonAsync("/auth/login",
            new { username = TaskFlowApplicationFactory.AdminUsername, password = "wrong-password" });

        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
    }
}
