using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Features.Shared;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Boards.ListBoards;

public static class ListBoardsEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/workspaces/{workspaceId:guid}/boards", HandleAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        Guid workspaceId,
        HttpContext context,
        AppDbContext db,
        WorkspaceAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUser.GetId(context.User);
        if (await authorization.FindMembershipAsync(workspaceId, userId, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        var boards = await db.Boards
            .AsNoTracking()
            .Where(board => board.WorkspaceId == workspaceId)
            .OrderBy(board => board.IsArchived)
            .ThenBy(board => board.CreatedAt)
            .ThenBy(board => board.Id)
            .Select(board => new BoardSummaryResponse(
                board.Id,
                board.WorkspaceId,
                board.Name,
                board.Description,
                board.Color,
                board.IsArchived,
                board.Tasks.Count,
                board.UpdatedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(boards);
    }
}
