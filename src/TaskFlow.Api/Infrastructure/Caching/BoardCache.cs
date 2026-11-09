using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;

namespace TaskFlow.Api.Infrastructure.Caching;

public sealed class BoardCache(IConnectionMultiplexer? redis, ILogger<BoardCache> logger)
{
    private const int ActivityLimit = 20;
    private const string ConditionalSetScript = """
        local current = redis.call('GET', KEYS[1])
        if not current then current = '0' end
        if current ~= ARGV[1] then return 0 end
        redis.call('SET', KEYS[2], ARGV[2], 'PX', ARGV[3])
        return 1
        """;
    private const string InvalidateScript = """
        redis.call('INCR', KEYS[1])
        redis.call('DEL', KEYS[2], KEYS[3])
        return 1
        """;
    private int _down;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerOptions.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public static string BoardKey(Guid boardId) => $"taskflow:boards:{boardId}";

    public static string StatsKey(Guid boardId) => $"taskflow:boards:{boardId}:stats";

    public static string ActivityKey(Guid boardId) => $"taskflow:boards:{boardId}:activity";

    public static string GenerationKey(Guid boardId) => $"taskflow:boards:{boardId}:generation";

    public async Task<(CacheStatus Status, string? Json, long Generation)> GetVersionedAsync(
        Guid boardId,
        string key)
    {
        if (redis is null)
        {
            return (CacheStatus.Off, null, 0);
        }

        try
        {
            var values = await redis.GetDatabase().StringGetAsync(
            [
                GenerationKey(boardId),
                key,
            ]);
            MarkHealthy();
            var generation = long.TryParse((string?)values[0], out var parsed) ? parsed : 0;
            return values[1].IsNull
                ? (CacheStatus.Miss, null, generation)
                : (CacheStatus.Hit, (string?)values[1], generation);
        }
        catch (Exception exception)
        {
            MarkDown(exception);
            return (CacheStatus.Down, null, 0);
        }
    }

    public async Task<bool> SetIfGenerationUnchangedAsync(
        Guid boardId,
        string key,
        string json,
        long generation,
        TimeSpan timeToLive)
    {
        if (redis is null)
        {
            return false;
        }

        try
        {
            var result = await redis.GetDatabase().ScriptEvaluateAsync(
                ConditionalSetScript,
                [GenerationKey(boardId), key],
                [generation, json, (long)timeToLive.TotalMilliseconds]);
            MarkHealthy();
            return (long)result == 1;
        }
        catch (Exception exception)
        {
            MarkDown(exception);
            return false;
        }
    }

    public async Task InvalidateAsync(Guid boardId)
    {
        if (redis is null)
        {
            return;
        }

        try
        {
            await redis.GetDatabase().ScriptEvaluateAsync(
                InvalidateScript,
                [GenerationKey(boardId), BoardKey(boardId), StatsKey(boardId)]);
            MarkHealthy();
        }
        catch (Exception exception)
        {
            MarkDown(exception);
        }
    }

    public async Task RecordActivityAsync(Guid boardId, string message)
    {
        if (redis is null)
        {
            return;
        }

        try
        {
            var entry = JsonSerializer.Serialize(
                new ActivityEntry(message, DateTimeOffset.UtcNow),
                Json);
            var database = redis.GetDatabase();
            await database.ListLeftPushAsync(ActivityKey(boardId), entry);
            await database.ListTrimAsync(ActivityKey(boardId), 0, ActivityLimit - 1);
            MarkHealthy();
        }
        catch (Exception exception)
        {
            MarkDown(exception);
        }
    }

    public async Task<(bool Available, IReadOnlyList<ActivityEntry> Entries)> GetActivityAsync(Guid boardId)
    {
        if (redis is null)
        {
            return (false, []);
        }

        try
        {
            var values = await redis.GetDatabase().ListRangeAsync(ActivityKey(boardId), 0, ActivityLimit - 1);
            MarkHealthy();
            var entries = new List<ActivityEntry>(values.Length);
            foreach (var value in values)
            {
                if (JsonSerializer.Deserialize<ActivityEntry>((string)value!, Json) is { } entry)
                {
                    entries.Add(entry);
                }
            }

            return (true, entries);
        }
        catch (Exception exception)
        {
            MarkDown(exception);
            return (false, []);
        }
    }

    private void MarkDown(Exception exception)
    {
        if (Interlocked.Exchange(ref _down, 1) == 0)
        {
            logger.LogWarning(
                "Cache unavailable - serving from Postgres. ({Message})",
                exception.Message);
        }
    }

    private void MarkHealthy()
    {
        if (Interlocked.Exchange(ref _down, 0) == 1)
        {
            logger.LogInformation("Cache connection recovered.");
        }
    }
}
