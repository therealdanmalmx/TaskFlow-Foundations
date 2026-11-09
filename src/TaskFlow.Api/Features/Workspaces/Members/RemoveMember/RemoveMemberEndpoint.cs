using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Domain.Workspaces;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Caching;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Workspaces.Members.RemoveMember;

public static class RemoveMemberEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapDelete("/api/workspaces/{workspaceId:guid}/members/{userId:guid}", HandleAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        Guid workspaceId,
        Guid userId,
        HttpContext context,
        AppDbContext db,
        WorkspaceAuthorizationService authorization,
        BoardCache cache,
        CancellationToken cancellationToken)
    {
        var actorId = CurrentUser.GetId(context.User);
        var actor = await authorization.FindMembershipAsync(workspaceId, actorId, cancellationToken);
        if (actor is null)
        {
            return Results.NotFound();
        }

        if (!WorkspaceAuthorizationService.CanManage(actor.Role) && actorId != userId)
        {
            return Results.Forbid();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await WorkspaceInvariantLock.AcquireAsync(db, workspaceId, cancellationToken);

        actor = await authorization.FindMembershipAsync(workspaceId, actorId, cancellationToken);
        if (actor is null)
        {
            return Results.NotFound();
        }

        if (!WorkspaceAuthorizationService.CanManage(actor.Role) && actorId != userId)
        {
            return Results.Forbid();
        }

        var target = await db.WorkspaceMemberships.SingleOrDefaultAsync(
            membership => membership.WorkspaceId == workspaceId && membership.UserId == userId,
            cancellationToken);
        if (target is null)
        {
            return Results.NotFound();
        }

        if (actor.Role == WorkspaceRole.Admin && target.Role == WorkspaceRole.Owner)
        {
            return Results.Forbid();
        }

        if (target.Role == WorkspaceRole.Owner)
        {
            var ownerCount = await db.WorkspaceMemberships.CountAsync(
                membership => membership.WorkspaceId == workspaceId && membership.Role == WorkspaceRole.Owner,
                cancellationToken);
            if (ownerCount == 1)
            {
                return Results.Conflict(new { error = "A workspace must always have at least one owner." });
            }
        }

        var affectedBoardIds = await db.Boards
            .Where(board => board.WorkspaceId == workspaceId &&
                board.Tasks.Any(task => task.AssignedToUserId == userId))
            .Select(board => board.Id)
            .OrderBy(boardId => boardId)
            .ToListAsync(cancellationToken);
        await BoardInvariantLock.AcquireManyAsync(db, affectedBoardIds, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        await db.Tasks
            .Where(task => task.Board.WorkspaceId == workspaceId && task.AssignedToUserId == userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(task => task.AssignedToUserId, (Guid?)null)
                    .SetProperty(task => task.UpdatedAt, now),
                cancellationToken);
        await db.Boards
            .Where(board => affectedBoardIds.Contains(board.Id))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(board => board.Revision, board => board.Revision + 1)
                    .SetProperty(
                        board => board.UpdatedAt,
                        board => board.UpdatedAt < now ? now : board.UpdatedAt),
                cancellationToken);
        db.WorkspaceMemberships.Remove(target);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var boardId in affectedBoardIds)
        {
            await cache.InvalidateAsync(boardId);
        }

        return Results.NoContent();
    }
}
