using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Domain.Tasks;
using TaskFlow.Api.Infrastructure.Caching;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Shared;

public static class StatsQueries
{
    private static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(30);

    public static async Task<(CacheStatus Status, BoardStatsResponse Stats)> GetAsync(
        AppDbContext db,
        BoardCache cache,
        Guid boardId,
        long boardRevision,
        CancellationToken cancellationToken = default)
    {
        var (status, cached, generation) = await cache.GetVersionedAsync(
            boardId,
            BoardCache.StatsKey(boardId));
        if (status == CacheStatus.Hit && cached is not null)
        {
            try
            {
                if (JsonSerializer.Deserialize<BoardStatsCacheEntry>(cached, BoardCache.Json) is
                    { Stats: not null } value && value.BoardRevision == boardRevision)
                {
                    return (status, value.Stats);
                }

                status = CacheStatus.Miss;
            }
            catch (JsonException)
            {
                status = CacheStatus.Miss;
            }
        }

        var startOfUtcDay = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTimeOffset.UtcNow;
        var snapshot = await db.Boards
            .AsNoTracking()
            .Where(board => board.Id == boardId)
            .Select(board => new
            {
                board.Revision,
                Todo = board.Tasks.Count(task => task.Status == TaskState.Todo),
                Doing = board.Tasks.Count(task => task.Status == TaskState.Doing),
                Done = board.Tasks.Count(task => task.Status == TaskState.Done),
                Total = board.Tasks.Count,
                DoneToday = board.Tasks.Count(task => task.CompletedAt >= startOfUtcDay),
                OldestActive = board.Tasks
                    .Where(task => task.Status != TaskState.Done)
                    .Min(task => (DateTimeOffset?)task.CreatedAt),
                Overdue = board.Tasks.Count(task => task.Status != TaskState.Done &&
                    task.DueDate != null && task.DueDate < today),
            })
            .SingleAsync(cancellationToken);

        var stats = new BoardStatsResponse(
            snapshot.Todo,
            snapshot.Doing,
            snapshot.Done,
            snapshot.Total,
            snapshot.DoneToday,
            snapshot.OldestActive is { } oldest
                ? Math.Round((now - oldest).TotalDays, 1)
                : null,
            snapshot.Overdue);

        await cache.SetIfGenerationUnchangedAsync(
            boardId,
            BoardCache.StatsKey(boardId),
            JsonSerializer.Serialize(
                new BoardStatsCacheEntry(snapshot.Revision, stats),
                BoardCache.Json),
            generation,
            TimeToLive);

        return (status, stats);
    }
}
