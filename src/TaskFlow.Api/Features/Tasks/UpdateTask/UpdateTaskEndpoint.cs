using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Features.Shared;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Caching;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Tasks.UpdateTask;

public static class UpdateTaskEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/api/tasks/{taskId:guid}", HandleAsync).RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        Guid taskId,
        UpdateTaskRequest request,
        HttpContext context,
        AppDbContext db,
        BoardCache cache,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var validationError = InputValidation.ValidateTask(request.Title, request.Description);
        if (validationError is not null)
        {
            return Results.BadRequest(new { error = validationError });
        }

        if (request.Priority is null || !Enum.IsDefined(request.Priority.Value))
        {
            return Results.BadRequest(new { error = "Priority must be Low, Medium, High, or Urgent." });
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

        if (request.AssigneeId is { } assigneeId &&
            !await db.WorkspaceMemberships.AnyAsync(
                membership => membership.WorkspaceId == task.Board.WorkspaceId && membership.UserId == assigneeId,
                cancellationToken))
        {
            return Results.BadRequest(new { error = "Assignee must be a member of this workspace." });
        }

        var now = DateTimeOffset.UtcNow;
        task.Title = request.Title!.Trim();
        task.Description = request.Description?.Trim() ?? string.Empty;
        task.Priority = request.Priority.Value;
        task.DueDate = request.DueDate;
        task.AssignedToUserId = request.AssigneeId;
        task.UpdatedAt = now;
        task.Board.MarkChanged(now);
        await db.SaveChangesAsync(cancellationToken);
        var result = await TaskQueries.FindResponseAsync(db, task.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await cache.InvalidateAsync(task.BoardId);
        await cache.RecordActivityAsync(task.BoardId, $"Updated \"{task.Title}\"");
        loggerFactory.CreateLogger("TaskFlow.Tasks")
            .LogInformation("Task {TaskId} updated on board {BoardId}.", task.Id, task.BoardId);

        return Results.Ok(result);
    }
}
