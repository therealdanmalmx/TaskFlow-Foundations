using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TaskFlow.Api.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Features.Authentication;

[Collection(TaskFlowApiCollection.Name)]
public class SessionCookieTests(TaskFlowTestEnvironment environment, ITestOutputHelper output)
    : IntegrationTestBase(environment, output)
{
    [Fact]
    public async Task Registration_and_login_cookies_authenticate_and_logout_clears_and_revokes_them()
    {
        using var client = Application.CreateIntegrationClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });
        var unique = Guid.NewGuid().ToString("N")[..10];
        var username = $"cookie-{unique}";
        var password = "cookie-password-123";
        using var registerRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/register")
        {
            Content = JsonContent.Create(new
            {
                username,
                email = $"{username}@example.test",
                displayName = $"Cookie {unique}",
                password,
            }),
        };
        registerRequest.Headers.Add("X-Forwarded-For", "198.51.100.250");

        var registered = await client.SendAsync(registerRequest);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var registration = await registered.Content.ReadFromJsonAsync<JsonElement>();
        var registrationToken = registration.GetProperty("token").GetString()!;
        AssertSessionCookieIsHttpOnlyAndStrict(registered);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me")).StatusCode);

        var loggedOut = await client.PostAsync("/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.NoContent, loggedOut.StatusCode);
        AssertSessionCookieWasCleared(loggedOut);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await SendAsync(registrationToken, HttpMethod.Get, "/api/users/me")).StatusCode);

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = JsonContent.Create(new { username, password }),
        };
        loginRequest.Headers.Add("X-Forwarded-For", "198.51.100.250");
        var loggedIn = await client.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.OK, loggedIn.StatusCode);
        AssertSessionCookieIsHttpOnlyAndStrict(loggedIn);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me")).StatusCode);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PostAsync("/auth/logout", content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(
        string token,
        HttpMethod method,
        string path,
        object? body = null)
    {
        return await Client.SendAuthenticatedAsync(token, method, path, body);
    }

    private static void AssertSessionCookieIsHttpOnlyAndStrict(HttpResponseMessage response)
    {
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("taskflow_session=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertSessionCookieWasCleared(HttpResponseMessage response)
    {
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("taskflow_session=", StringComparison.Ordinal));
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);
    }
}
