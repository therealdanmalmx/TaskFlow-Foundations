using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Infrastructure.Caching;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Shared;

public static class BoardQueries
{
    private static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(60);

    public static async Task<(CacheStatus Status, BoardDataResponse? Board)> GetAsync(
        AppDbContext db,
        BoardCache cache,
        Guid boardId,
        long boardRevision,
        CancellationToken cancellationToken = default)
    {
        var (status, cached, generation) = await cache.GetVersionedAsync(
            boardId,
            BoardCache.BoardKey(boardId));
        if (status == CacheStatus.Hit && cached is not null)
        {
            try
            {
                if (JsonSerializer.Deserialize<BoardCacheEntry>(cached, BoardCache.Json) is
                    { Board: not null } value && value.BoardRevision == boardRevision)
                {
                    return (status, value.Board);
                }

                status = CacheStatus.Miss;
            }
            catch (JsonException)
            {
                status = CacheStatus.Miss;
            }
        }

        var entry = await db.Boards
            .AsNoTracking()
            .Where(candidate => candidate.Id == boardId)
            .Select(candidate => new BoardCacheEntry(
                candidate.Revision,
                new BoardDataResponse(
                    candidate.Id,
                    candidate.WorkspaceId,
                    candidate.Name,
                    candidate.Description,
                    candidate.Color,
                    candidate.IsArchived,
                    candidate.Tasks.Count,
                    candidate.UpdatedAt,
                    candidate.Tasks
                        .OrderBy(task => task.Position)
                        .ThenBy(task => task.Id)
                        .Select(task => new TaskResponse(
                            task.Id,
                            task.BoardId,
                            task.Title,
                            task.Description,
                            task.Status,
                            task.Priority,
                            task.DueDate,
                            task.Position,
                            task.CreatedByUserId,
                            task.CreatedByUser.DisplayName,
                            task.AssignedToUserId,
                            task.AssignedToUser == null ? null : task.AssignedToUser.DisplayName,
                            task.CreatedAt,
                            task.UpdatedAt,
                            task.CompletedAt))
                        .ToList())))
            .SingleOrDefaultAsync(cancellationToken);

        if (entry is not null)
        {
            await cache.SetIfGenerationUnchangedAsync(
                boardId,
                BoardCache.BoardKey(boardId),
                JsonSerializer.Serialize(entry, BoardCache.Json),
                generation,
                TimeToLive);
        }

        return (status, entry?.Board);
    }
}
