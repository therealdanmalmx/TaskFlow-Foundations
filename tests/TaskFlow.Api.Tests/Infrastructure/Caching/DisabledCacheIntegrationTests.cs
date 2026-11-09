using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TaskFlow.Api.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Infrastructure.Caching;

[Collection(TaskFlowApiCollection.Name)]
public sealed class DisabledCacheIntegrationTests(
    TaskFlowTestEnvironment environment,
    ITestOutputHelper output) : IntegrationTestBase(environment, output)
{
    [Fact]
    public async Task Without_Redis_board_and_stats_use_Postgres_and_activity_is_unavailable()
    {
        var owner = await Application.CreateUserScenarioAsync("cache-off", createPersonalWorkspace: true);
        var boardId = owner.BoardId!.Value;
        using var created = await Client.SendAuthenticatedAsync(
            owner.Token,
            HttpMethod.Post,
            $"/api/boards/{boardId}/tasks",
            new { title = "Durable without Redis" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var board = await Client.SendAuthenticatedAsync(
            owner.Token, HttpMethod.Get, $"/api/boards/{boardId}");
        Assert.Equal(HttpStatusCode.OK, board.StatusCode);
        Assert.Equal("OFF", board.Headers.GetValues("X-Cache").Single());
        Assert.Contains("Durable without Redis", await board.Content.ReadAsStringAsync());

        using var stats = await Client.SendAuthenticatedAsync(
            owner.Token, HttpMethod.Get, $"/api/boards/{boardId}/stats");
        Assert.Equal(HttpStatusCode.OK, stats.StatusCode);
        Assert.Equal("OFF", stats.Headers.GetValues("X-Cache").Single());
        var counts = await stats.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, counts.GetProperty("total").GetInt32());
        Assert.Equal(1, counts.GetProperty("todo").GetInt32());
        Assert.Equal(0, counts.GetProperty("doing").GetInt32());
        Assert.Equal(0, counts.GetProperty("done").GetInt32());

        using var activity = await Client.SendAuthenticatedAsync(
            owner.Token, HttpMethod.Get, $"/api/boards/{boardId}/activity");
        Assert.Equal(HttpStatusCode.OK, activity.StatusCode);
        var feed = await activity.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(feed.GetProperty("available").GetBoolean());
        Assert.Empty(feed.GetProperty("entries").EnumerateArray());
    }
}
