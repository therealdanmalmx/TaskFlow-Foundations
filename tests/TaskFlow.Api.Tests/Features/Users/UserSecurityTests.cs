using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TaskFlow.Api.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Features.Users;

[Collection(TaskFlowApiCollection.Name)]
public class UserSecurityTests(
    TaskFlowTestEnvironment environment,
    ITestOutputHelper output) : IntegrationTestBase(environment, output)
{
    [Fact]
    public async Task Profile_updates_enforce_normalized_email_uniqueness_and_ignore_privileged_fields()
    {
        var adminToken = await Application.CreateAdminTokenAsync();
        var existing = await Application.CreateUserScenarioAsync("profile-collision");
        var existingEmail = $"{existing.Username}@example.test";

        using var duplicateEmail = await SendAsync(
            adminToken,
            HttpMethod.Put,
            "/api/users/me",
            new
            {
                displayName = "Platform Administrator",
                email = $"  {existingEmail.ToUpperInvariant()}  ",
            });
        Assert.Equal(HttpStatusCode.Conflict, duplicateEmail.StatusCode);

        using var invalidName = await SendAsync(
            adminToken,
            HttpMethod.Put,
            "/api/users/me",
            new { displayName = " ", email = "admin-updated@example.test" });
        using var invalidEmail = await SendAsync(
            adminToken,
            HttpMethod.Put,
            "/api/users/me",
            new { displayName = "Platform Administrator", email = "invalid@@example.test" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidName.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidEmail.StatusCode);

        var newEmail = $"admin-{Guid.NewGuid():N}@example.test";
        using var overposted = await SendAsync(
            adminToken,
            HttpMethod.Put,
            "/api/users/me",
            new
            {
                displayName = "  Platform Administrator  ",
                email = $"  {newEmail}  ",
                username = "hijacked-admin",
                isActive = false,
                tokenVersion = int.MaxValue,
            });
        Assert.Equal(HttpStatusCode.OK, overposted.StatusCode);
        var updated = await overposted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            TaskFlowApplicationFactory.AdminUsername,
            updated.GetProperty("username").GetString());
        Assert.Equal("Platform Administrator", updated.GetProperty("displayName").GetString());
        Assert.Equal(newEmail, updated.GetProperty("email").GetString());

        var persisted = await GetJsonAsync(adminToken, "/api/users/me");
        Assert.Equal(
            TaskFlowApplicationFactory.AdminUsername,
            persisted.GetProperty("username").GetString());
        Assert.Equal("Platform Administrator", persisted.GetProperty("displayName").GetString());
        Assert.Equal(newEmail, persisted.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Password_change_limit_is_partitioned_by_user_and_effective_client_address()
    {
        var adminToken = await Application.CreateAdminTokenAsync();
        var client = Client;
        const string limitedAddress = "198.18.30.30";

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var rejected = await SendAsync(
                adminToken,
                HttpMethod.Put,
                "/api/users/me/password",
                new { currentPassword = "", newPassword = "replacement-password-123" },
                client,
                limitedAddress);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        using var throttled = await SendAsync(
            adminToken,
            HttpMethod.Put,
            "/api/users/me/password",
            new { currentPassword = "", newPassword = "replacement-password-123" },
            client,
            limitedAddress);
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);

        using var differentAddress = await SendAsync(
            adminToken,
            HttpMethod.Put,
            "/api/users/me/password",
            new { currentPassword = "", newPassword = "replacement-password-123" },
            client,
            "198.18.30.31");
        Assert.Equal(HttpStatusCode.BadRequest, differentAddress.StatusCode);
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
}
