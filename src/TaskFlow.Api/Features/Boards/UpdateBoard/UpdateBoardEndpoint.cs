using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Features.Shared;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Caching;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Boards.UpdateBoard;

public static class UpdateBoardEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/api/boards/{boardId:guid}", HandleAsync).RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        Guid boardId,
        UpdateBoardRequest request,
        HttpContext context,
        AppDbContext db,
        BoardCache cache,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUser.GetId(context.User);
        var boardPreview = await db.Boards
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == boardId &&
                    candidate.Workspace.Memberships.Any(membership => membership.UserId == userId),
                cancellationToken);
        if (boardPreview is null)
        {
            return Results.NotFound();
        }

        var membershipPreview = await db.WorkspaceMemberships
            .AsNoTracking()
            .SingleOrDefaultAsync(
                membership => membership.WorkspaceId == boardPreview.WorkspaceId &&
                    membership.UserId == userId,
                cancellationToken);
        if (membershipPreview is null)
        {
            return Results.NotFound();
        }

        if (!WorkspaceAuthorizationService.CanManage(membershipPreview.Role))
        {
            return Results.Forbid();
        }

        var validationError = InputValidation.ValidateBoard(
            request.Name,
            request.Description,
            request.Color);
        if (validationError is not null)
        {
            return Results.BadRequest(new { error = validationError });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await WorkspaceInvariantLock.AcquireAsync(db, boardPreview.WorkspaceId, cancellationToken);
        await BoardInvariantLock.AcquireAsync(db, boardId, cancellationToken);

        var board = await db.Boards.SingleOrDefaultAsync(
            candidate => candidate.Id == boardId,
            cancellationToken);
        if (board is null)
        {
            return Results.NotFound();
        }

        var membership = await db.WorkspaceMemberships
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.WorkspaceId == board.WorkspaceId &&
                    candidate.UserId == userId,
                cancellationToken);
        if (membership is null)
        {
            return Results.NotFound();
        }

        if (!WorkspaceAuthorizationService.CanManage(membership.Role))
        {
            return Results.Forbid();
        }

        board.Name = request.Name!.Trim();
        board.Description = request.Description?.Trim() ?? string.Empty;
        board.Color = request.Color!.Trim().ToLowerInvariant();
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
        return Results.Ok(BoardDetailResponse.From(updated!, membership.Role));
    }
}
