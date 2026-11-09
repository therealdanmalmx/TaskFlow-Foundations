using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Features.Shared;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Caching;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Activity.GetBoardActivity;

public static class GetBoardActivityEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/boards/{boardId:guid}/activity", HandleAsync).RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        Guid boardId,
        HttpContext context,
        AppDbContext db,
        BoardCache cache,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUser.GetId(context.User);
        var canAccess = await db.Boards.AsNoTracking().AnyAsync(
            board => board.Id == boardId &&
                board.Workspace.Memberships.Any(membership => membership.UserId == userId),
            cancellationToken);
        if (!canAccess)
        {
            return Results.NotFound();
        }

        var (available, entries) = await cache.GetActivityAsync(boardId);
        return Results.Ok(new ActivityResponse(available, entries));
    }
}
