using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Shared;

public static class TaskQueries
{
    public static Task<TaskResponse> FindResponseAsync(
        AppDbContext db,
        Guid taskId,
        CancellationToken cancellationToken = default) =>
        db.Tasks
            .AsNoTracking()
            .Where(task => task.Id == taskId)
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
            .SingleAsync(cancellationToken);
}
