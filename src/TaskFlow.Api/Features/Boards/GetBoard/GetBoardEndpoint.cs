using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Features.Shared;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Caching;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Boards.GetBoard;

public static class GetBoardEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/boards/{boardId:guid}", HandleAsync).RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        Guid boardId,
        HttpContext context,
        HttpResponse response,
        AppDbContext db,
        BoardCache cache,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUser.GetId(context.User);
        var access = await db.WorkspaceMemberships
            .AsNoTracking()
            .Where(candidate => candidate.UserId == userId &&
                candidate.Workspace.Boards.Any(board => board.Id == boardId))
            .Select(candidate => new
            {
                candidate.Role,
                BoardRevision = candidate.Workspace.Boards
                    .Where(board => board.Id == boardId)
                    .Select(board => board.Revision)
                    .Single(),
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (access is null)
        {
            return Results.NotFound();
        }

        var (status, board) = await BoardQueries.GetAsync(
            db,
            cache,
            boardId,
            access.BoardRevision,
            cancellationToken);
        response.Headers["X-Cache"] = status.ToString().ToUpperInvariant();
        return board is null
            ? Results.NotFound()
            : Results.Ok(BoardDetailResponse.From(board, access.Role));
    }
}
