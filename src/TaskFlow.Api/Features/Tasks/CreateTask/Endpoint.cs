using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Domain.Tasks;
using TaskFlow.Api.Features.Shared;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Caching;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Tasks.CreateTask;

public static class Endpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/boards/{boardId:guid}/tasks", HandleAsync).RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        Guid boardId,
        CreateTaskRequest request,
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

        var priority = request.Priority ?? TaskPriority.Medium;
        if (!Enum.IsDefined(priority))
        {
            return Results.BadRequest(new { error = "Priority must be Low, Medium, High, or Urgent." });
        }

        var userId = CurrentUser.GetId(context.User);
        var boardPreview = await db.Boards
            .AsNoTracking()
            .Where(candidate => candidate.Id == boardId &&
                candidate.Workspace.Memberships.Any(membership => membership.UserId == userId))
            .Select(candidate => new { candidate.WorkspaceId })
            .SingleOrDefaultAsync(cancellationToken);
        if (boardPreview is null)
        {
            return Results.NotFound();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await WorkspaceInvariantLock.AcquireAsync(db, boardPreview.WorkspaceId, cancellationToken);
        await BoardInvariantLock.AcquireAsync(db, boardId, cancellationToken);

        var board = await db.Boards.SingleOrDefaultAsync(
            candidate => candidate.Id == boardId &&
                candidate.Workspace.Memberships.Any(membership => membership.UserId == userId),
            cancellationToken);
        if (board is null)
        {
            return Results.NotFound();
        }

        if (board.IsArchived)
        {
            return Results.Conflict(new { error = "Archived boards are read-only." });
        }

        if (request.AssigneeId is { } assigneeId &&
            !await db.WorkspaceMemberships.AnyAsync(
                membership => membership.WorkspaceId == board.WorkspaceId && membership.UserId == assigneeId,
                cancellationToken))
        {
            return Results.BadRequest(new { error = "Assignee must be a member of this workspace." });
        }

        var nextPosition = (await db.Tasks
            .Where(task => task.BoardId == boardId)
            .MaxAsync(task => (int?)task.Position, cancellationToken) ?? -1) + 1;
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem
        {
            BoardId = boardId,
            Title = request.Title!.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            Priority = priority,
            DueDate = request.DueDate,
            Position = nextPosition,
            CreatedByUserId = userId,
            AssignedToUserId = request.AssigneeId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        board.MarkChanged(now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);
        var result = await TaskQueries.FindResponseAsync(db, task.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await cache.InvalidateAsync(boardId);
        await cache.RecordActivityAsync(boardId, $"Created \"{task.Title}\"");
        loggerFactory.CreateLogger("TaskFlow.Tasks")
            .LogInformation("Task {TaskId} created on board {BoardId}.", task.Id, boardId);

        return Results.Created($"/api/tasks/{task.Id}", result);
    }
}
