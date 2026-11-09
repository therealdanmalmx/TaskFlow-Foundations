using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Features.Shared;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Caching;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Boards.ArchiveBoard;

public static class ArchiveBoardEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/api/boards/{boardId:guid}/archive", HandleAsync).RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        Guid boardId,
        ArchiveBoardRequest request,
        HttpContext context,
        AppDbContext db,
        BoardCache cache,
        CancellationToken cancellationToken)
    {
        if (request.IsArchived is null)
        {
            return Results.BadRequest(new { error = "isArchived is required." });
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

        var rolePreview = await db.WorkspaceMemberships
            .Where(membership => membership.WorkspaceId == boardPreview.WorkspaceId &&
                membership.UserId == userId)
            .Select(membership => membership.Role)
            .SingleAsync(cancellationToken);
        if (!WorkspaceAuthorizationService.CanManage(rolePreview))
        {
            return Results.Forbid();
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

        var role = await db.WorkspaceMemberships
            .Where(membership => membership.WorkspaceId == board.WorkspaceId && membership.UserId == userId)
            .Select(membership => membership.Role)
            .SingleAsync(cancellationToken);
        if (!WorkspaceAuthorizationService.CanManage(role))
        {
            return Results.Forbid();
        }

        board.IsArchived = request.IsArchived.Value;
        board.MarkChanged(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await cache.InvalidateAsync(board.Id);

        var (_, updated) = await BoardQueries.GetAsync(
            db,
            cache,
            board.Id,
            board.Revision,
            cancellationToken);
        return Results.Ok(BoardDetailResponse.From(updated!, role));
    }
}
