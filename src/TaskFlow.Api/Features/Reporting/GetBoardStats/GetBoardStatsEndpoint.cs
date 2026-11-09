using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Features.Shared;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Caching;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Reporting.GetBoardStats;

public static class GetBoardStatsEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/boards/{boardId:guid}/stats", HandleAsync).RequireAuthorization();
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
        var boardRevision = await db.Boards
            .AsNoTracking()
            .Where(board => board.Id == boardId &&
                board.Workspace.Memberships.Any(membership => membership.UserId == userId))
            .Select(board => (long?)board.Revision)
            .SingleOrDefaultAsync(cancellationToken);
        if (boardRevision is null)
        {
            return Results.NotFound();
        }

        var (status, stats) = await StatsQueries.GetAsync(
            db,
            cache,
            boardId,
            boardRevision.Value,
            cancellationToken);
        response.Headers["X-Cache"] = status.ToString().ToUpperInvariant();
        return Results.Ok(stats);
    }
}
