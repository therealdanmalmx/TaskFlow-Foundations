using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Caching;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Tasks.DeleteTask;

public static class DeleteTaskEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapDelete("/api/tasks/{taskId:guid}", HandleAsync).RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        Guid taskId,
        HttpContext context,
        AppDbContext db,
        BoardCache cache,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
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

        var boardId = task.BoardId;
        task.Board.MarkChanged(DateTimeOffset.UtcNow);
        db.Tasks.Remove(task);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await cache.InvalidateAsync(boardId);
        await cache.RecordActivityAsync(boardId, $"Deleted \"{task.Title}\"");
        loggerFactory.CreateLogger("TaskFlow.Tasks")
            .LogInformation("Task {TaskId} deleted from board {BoardId}.", task.Id, boardId);
        return Results.NoContent();
    }
}
