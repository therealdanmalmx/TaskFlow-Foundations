using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Api.Infrastructure.Caching;
using TaskFlow.Api.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Infrastructure.Caching;

[Collection(TaskFlowApiCollection.Name)]
public class TenantCacheBehaviorTests(TaskFlowTestEnvironment environment, ITestOutputHelper output)
    : IntegrationTestBase(environment, output, useRedis: true)
{
    [Fact]
    public async Task Board_cache_hits_and_invalidation_are_scoped_by_board_id()
    {
        var first = await Application.CreateUserScenarioAsync("cache-first", createPersonalWorkspace: true);
        var second = await Application.CreateUserScenarioAsync("cache-second", createPersonalWorkspace: true);
        var firstBoard = first.BoardId!.Value;
        var secondBoard = second.BoardId!.Value;

        var firstMiss = await Client.SendAuthenticatedAsync(first.Token, HttpMethod.Get, $"/api/boards/{firstBoard}");
        var firstHit = await Client.SendAuthenticatedAsync(first.Token, HttpMethod.Get, $"/api/boards/{firstBoard}");
        var secondMiss = await Client.SendAuthenticatedAsync(second.Token, HttpMethod.Get, $"/api/boards/{secondBoard}");
        var secondHit = await Client.SendAuthenticatedAsync(second.Token, HttpMethod.Get, $"/api/boards/{secondBoard}");
        Assert.Equal("MISS", CacheHeader(firstMiss));
        Assert.Equal("HIT", CacheHeader(firstHit));
        Assert.Equal("MISS", CacheHeader(secondMiss));
        Assert.Equal("HIT", CacheHeader(secondHit));

        var write = await Client.SendAuthenticatedAsync(
            first.Token,
            HttpMethod.Post,
            $"/api/boards/{firstBoard}/tasks",
            new { title = "Invalidate one board", description = "", priority = "Medium" });
        Assert.Equal(HttpStatusCode.Created, write.StatusCode);

        var firstAfterWrite = await Client.SendAuthenticatedAsync(
            first.Token,
            HttpMethod.Get,
            $"/api/boards/{firstBoard}");
        var secondAfterWrite = await Client.SendAuthenticatedAsync(
            second.Token,
            HttpMethod.Get,
            $"/api/boards/{secondBoard}");
        Assert.Equal("MISS", CacheHeader(firstAfterWrite));
        Assert.Equal("HIT", CacheHeader(secondAfterWrite));

        var cache = Application.Services.GetRequiredService<BoardCache>();
        var (_, _, staleGeneration) = await cache.GetVersionedAsync(
            firstBoard,
            BoardCache.BoardKey(firstBoard));
        await cache.InvalidateAsync(firstBoard);
        var staleValueWasStored = await cache.SetIfGenerationUnchangedAsync(
            firstBoard,
            BoardCache.BoardKey(firstBoard),
            "{\"name\":\"stale\"}",
            staleGeneration,
            TimeSpan.FromMinutes(1));
        var (statusAfterStaleWrite, _, _) = await cache.GetVersionedAsync(
            firstBoard,
            BoardCache.BoardKey(firstBoard));
        Assert.False(staleValueWasStored);
        Assert.Equal(CacheStatus.Miss, statusAfterStaleWrite);
    }

    private static string CacheHeader(HttpResponseMessage response) =>
        response.Headers.GetValues("X-Cache").Single();
}
