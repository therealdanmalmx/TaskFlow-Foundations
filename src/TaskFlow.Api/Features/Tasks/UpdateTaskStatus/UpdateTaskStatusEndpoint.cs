using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Domain.Tasks;
using TaskFlow.Api.Features.Shared;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Caching;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Tasks.UpdateTaskStatus;

public static class UpdateTaskStatusEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/api/tasks/{taskId:guid}/status", HandleAsync).RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        Guid taskId,
        UpdateTaskStatusRequest request,
        HttpContext context,
        AppDbContext db,
        BoardCache cache,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (request.Status is null || !Enum.IsDefined(request.Status.Value))
        {
            return Results.BadRequest(new { error = "Status must be Todo, Doing, or Done." });
        }

        if (request.Placement is not null and not "top" and not "bottom" and not "before")
        {
            return Results.BadRequest(new { error = "Placement must be top, bottom, or before." });
        }

        if ((request.Placement == "before") != request.BeforeTaskId.HasValue || request.BeforeTaskId == taskId)
        {
            return Results.BadRequest(new { error = "Before placement requires the ID of another task." });
        }

        var userId = CurrentUser.GetId(context.User);
        var taskPreview = await db.Tasks
            .AsNoTracking()
            .Where(candidate => candidate.Id == taskId &&
                candidate.Board.Workspace.Memberships.Any(membership => membership.UserId == userId))
            .Select(candidate => new
            {
                candidate.BoardId,
                candidate.Board.WorkspaceId,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (taskPreview is null)
        {
            return Results.NotFound();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await WorkspaceInvariantLock.AcquireAsync(db, taskPreview.WorkspaceId, cancellationToken);
        await BoardInvariantLock.AcquireAsync(db, taskPreview.BoardId, cancellationToken);

        var task = await db.Tasks
            .Include(candidate => candidate.Board)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == taskId &&
                    candidate.Board.Workspace.Memberships.Any(membership => membership.UserId == userId),
                cancellationToken);
        if (task is null)
        {
            return Results.NotFound();
        }

        if (task.Board.IsArchived)
        {
            return Results.Conflict(new { error = "Archived boards are read-only." });
        }

        var now = DateTimeOffset.UtcNow;
        var status = request.Status.Value;
        if (request.Placement is not null)
        {
            var destinationTasks = await db.Tasks
                .Where(candidate => candidate.BoardId == task.BoardId &&
                    candidate.Status == status && candidate.Id != task.Id)
                .OrderBy(candidate => candidate.Position)
                .ThenBy(candidate => candidate.Id)
                .ToListAsync(cancellationToken);
            var insertionIndex = request.Placement switch
            {
                "top" => 0,
                "before" => destinationTasks.FindIndex(candidate => candidate.Id == request.BeforeTaskId),
                _ => destinationTasks.Count,
            };
            if (insertionIndex < 0)
            {
                return Results.Conflict(new { error = "The destination task has moved or is no longer available. Refresh the board and try again." });
            }

            destinationTasks.Insert(insertionIndex, task);
            for (var index = 0; index < destinationTasks.Count; index++)
            {
                destinationTasks[index].Position = index;
            }
        }

        if (status == TaskState.Done && task.Status != TaskState.Done)
        {
            task.CompletedAt = now;
        }
        else if (status != TaskState.Done)
        {
            task.CompletedAt = null;
        }

        task.Status = status;
        task.UpdatedAt = now;
        task.Board.MarkChanged(now);
        await db.SaveChangesAsync(cancellationToken);
        var result = await TaskQueries.FindResponseAsync(db, task.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await cache.InvalidateAsync(task.BoardId);
        await cache.RecordActivityAsync(task.BoardId, $"Moved \"{task.Title}\" to {status}");
        loggerFactory.CreateLogger("TaskFlow.Tasks")
            .LogInformation(
                "Task {TaskId} moved to {TaskStatus} on board {BoardId}.",
                task.Id,
                status,
                task.BoardId);

        return Results.Ok(result);
    }
}
