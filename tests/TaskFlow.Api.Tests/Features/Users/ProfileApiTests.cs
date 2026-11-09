using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TaskFlow.Api.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Features.Users;

[Collection(TaskFlowApiCollection.Name)]
public class ProfileApiTests(TaskFlowTestEnvironment environment, ITestOutputHelper output)
    : IntegrationTestBase(environment, output)
{
    [Fact]
    public async Task Profile_and_password_changes_update_identity_and_revoke_the_old_session()
    {
        var user = await Application.CreateUserScenarioAsync("profile");
        var newEmail = $"updated-{Guid.NewGuid():N}@example.test";
        var updated = await SendAsync(
            user.Token,
            HttpMethod.Put,
            "/api/users/me",
            new { displayName = "Updated Person", email = newEmail });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var profile = await updated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Updated Person", profile.GetProperty("displayName").GetString());
        Assert.Equal(newEmail, profile.GetProperty("email").GetString());

        var newPassword = "replacement-password-123";
        var changed = await SendAsync(
            user.Token,
            HttpMethod.Put,
            "/api/users/me/password",
            new { currentPassword = user.Password, newPassword });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var session = await changed.Content.ReadFromJsonAsync<JsonElement>();
        var newToken = session.GetProperty("token").GetString()!;

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await SendAsync(user.Token, HttpMethod.Get, "/api/users/me")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await SendAsync(newToken, HttpMethod.Get, "/api/users/me")).StatusCode);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await SendAsync(
                    newToken,
                    HttpMethod.Put,
                    "/api/users/me/password",
                    new { currentPassword = "not-the-current-password", newPassword })).StatusCode);
        }

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await SendAsync(
                newToken,
                HttpMethod.Put,
                "/api/users/me/password",
                new { currentPassword = "not-the-current-password", newPassword })).StatusCode);

        var oldLogin = await Client.PostAsJsonAsync(
            "/auth/login",
            new { username = user.Username, password = user.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        var newLogin = await Client.PostAsJsonAsync(
            "/auth/login",
            new { username = user.Username, password = newPassword });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
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
