using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Persistence;
using TaskFlow.Api.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Features.Users;

[Collection(TaskFlowApiCollection.Name)]
public class UserSessionConcurrencyTests(
    TaskFlowTestEnvironment environment,
    ITestOutputHelper output) : IntegrationTestBase(environment, output)
{
    private const int UserLockNamespace = 0x55534552;

    [Fact]
    public async Task Concurrent_password_changes_with_the_same_credentials_have_one_winner()
    {
        var user = await Application.CreateUserScenarioAsync("password-race");
        const string firstPassword = "concurrent-password-alpha-123";
        const string secondPassword = "concurrent-password-bravo-123";
        var responses = await RunBehindUserLockAsync(
            user.UserId,
            () => Client.SendAuthenticatedAsync(
                user.Token,
                HttpMethod.Put,
                "/api/users/me/password",
                new { currentPassword = user.Password, newPassword = firstPassword }),
            () => Client.SendAuthenticatedAsync(
                user.Token,
                HttpMethod.Put,
                "/api/users/me/password",
                new { currentPassword = user.Password, newPassword = secondPassword }));

        try
        {
            var successfulIndex = Array.FindIndex(
                responses,
                response => response.StatusCode == HttpStatusCode.OK);
            Assert.True(successfulIndex >= 0, "Exactly one password change should succeed.");
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            var rejected = Assert.Single(
                responses,
                response => response.StatusCode == HttpStatusCode.BadRequest);
            var error = await rejected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Current password is incorrect.", error.GetProperty("error").GetString());

            var winningPassword = successfulIndex == 0 ? firstPassword : secondPassword;
            var losingPassword = successfulIndex == 0 ? secondPassword : firstPassword;
            var session = await responses[successfulIndex].Content.ReadFromJsonAsync<JsonElement>();
            var replacementToken = session.GetProperty("token").GetString()!;
            Assert.Equal(1, ReadTokenVersion(replacementToken));
            Assert.Equal(1, await GetTokenVersionAsync(user.UserId));

            Assert.Equal(HttpStatusCode.Unauthorized, await ProfileStatusAsync(user.Token));
            Assert.Equal(HttpStatusCode.OK, await ProfileStatusAsync(replacementToken));
            Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(user.Username, losingPassword));
            Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(user.Username, winningPassword));
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
    public async Task Password_change_racing_logout_has_a_valid_serial_order_without_lost_revocation()
    {
        var user = await Application.CreateUserScenarioAsync("logout-race");
        const string newPassword = "logout-race-password-123";
        var responses = await RunBehindUserLockAsync(
            user.UserId,
            () => Client.SendAuthenticatedAsync(
                user.Token,
                HttpMethod.Put,
                "/api/users/me/password",
                new { currentPassword = user.Password, newPassword }),
            () => Client.SendAuthenticatedAsync(
                user.Token,
                HttpMethod.Post,
                "/auth/logout"));

        try
        {
            Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, responses[1].StatusCode);
            Assert.Equal(2, await GetTokenVersionAsync(user.UserId));

            var session = await responses[0].Content.ReadFromJsonAsync<JsonElement>();
            var replacementToken = session.GetProperty("token").GetString()!;
            var replacementVersion = ReadTokenVersion(replacementToken);
            Assert.Contains(replacementVersion, new[] { 1, 2 });

            Assert.Equal(HttpStatusCode.Unauthorized, await ProfileStatusAsync(user.Token));
            var replacementStatus = await ProfileStatusAsync(replacementToken);
            Assert.Equal(
                replacementVersion == 2 ? HttpStatusCode.OK : HttpStatusCode.Unauthorized,
                replacementStatus);

            Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(user.Username, user.Password));
            Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(user.Username, newPassword));
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
    public async Task Login_queued_behind_password_change_rechecks_credentials_under_the_user_lock()
    {
        var user = await Application.CreateUserScenarioAsync("login-password-race");
        const string newPassword = "login-race-password-123";
        var responses = await RunBehindUserLockAsync(
            user.UserId,
            () => Client.SendAuthenticatedAsync(
                user.Token,
                HttpMethod.Put,
                "/api/users/me/password",
                new { currentPassword = user.Password, newPassword }),
            () => Client.PostAsJsonAsync(
                "/auth/login",
                new { username = user.Username, password = user.Password }));

        try
        {
            Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, responses[1].StatusCode);
            Assert.Equal(1, await GetTokenVersionAsync(user.UserId));

            var replacementSession = await responses[0].Content.ReadFromJsonAsync<JsonElement>();
            var replacementToken = replacementSession.GetProperty("token").GetString()!;
            Assert.Equal(1, ReadTokenVersion(replacementToken));
            Assert.Equal(HttpStatusCode.Unauthorized, await ProfileStatusAsync(user.Token));
            Assert.Equal(HttpStatusCode.OK, await ProfileStatusAsync(replacementToken));
            Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(user.Username, user.Password));
            Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(user.Username, newPassword));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    private async Task<HttpResponseMessage[]> RunBehindUserLockAsync(
        Guid userId,
        params Func<Task<HttpResponseMessage>>[] requests)
    {
        using var blockerScope = Application.Services.CreateScope();
        var blockerDb = blockerScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var blockerTransaction = await blockerDb.Database.BeginTransactionAsync();
        await UserInvariantLock.AcquireAsync(blockerDb, userId);
        List<Task<HttpResponseMessage>> requestTasks = [];
        var transactionCompleted = false;

        try
        {
            foreach (var request in requests)
            {
                requestTasks.Add(request());
                await WaitForAdvisoryLockWaitersAsync(userId, requestTasks.Count);
            }

            await blockerTransaction.CommitAsync();
            transactionCompleted = true;
            return await Task.WhenAll(requestTasks);
        }
        catch
        {
            if (!transactionCompleted)
            {
                await blockerTransaction.RollbackAsync();
            }

            try
            {
                var responses = await Task.WhenAll(requestTasks).WaitAsync(TimeSpan.FromSeconds(5));
                foreach (var response in responses)
                {
                    response.Dispose();
                }
            }
            catch
            {
            }

            throw;
        }
    }

    private async Task WaitForAdvisoryLockWaitersAsync(Guid userId, int expectedCount)
    {
        var lockKey = EntityInvariantLock.GetKey(UserLockNamespace, userId);
        using var observerScope = Application.Services.CreateScope();
        var observerDb = observerScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stopwatch = Stopwatch.StartNew();
        var observedCount = 0;

        while (stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            observedCount = await observerDb.Database.SqlQuery<int>($"""
                SELECT COUNT(*)::integer AS "Value"
                FROM pg_locks
                WHERE locktype = 'advisory'
                  AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
                  AND NOT granted
                  AND classid::bigint = {(long)(uint)(lockKey >> 32)}
                  AND objid::bigint = {(long)(uint)lockKey}
                  AND objsubid = 1
                """).SingleAsync();
            if (observedCount >= expectedCount)
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException(
            $"Expected {expectedCount} requests to wait for the user lock, observed {observedCount}.");
    }

    private async Task<int> GetTokenVersionAsync(Guid userId)
    {
        using var scope = Application.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users
            .Where(user => user.Id == userId)
            .Select(user => user.TokenVersion)
            .SingleAsync();
    }

    private async Task<HttpStatusCode> ProfileStatusAsync(string token)
    {
        using var response = await Client.SendAuthenticatedAsync(
            token,
            HttpMethod.Get,
            "/api/users/me");
        return response.StatusCode;
    }

    private async Task<HttpStatusCode> LoginStatusAsync(string username, string password)
    {
        using var response = await Client.PostAsJsonAsync(
            "/auth/login",
            new { username, password });
        return response.StatusCode;
    }

    private static int ReadTokenVersion(string token)
    {
        var payload = token.Split('.')[1]
            .Replace('-', '+')
            .Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        var claim = document.RootElement.GetProperty(TokenService.TokenVersionClaim);
        return claim.ValueKind == JsonValueKind.String
            ? int.Parse(claim.GetString()!, System.Globalization.CultureInfo.InvariantCulture)
            : claim.GetInt32();
    }
}
