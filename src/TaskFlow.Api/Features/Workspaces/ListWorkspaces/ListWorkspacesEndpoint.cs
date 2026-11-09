using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Features.Shared;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Workspaces.ListWorkspaces;

public static class ListWorkspacesEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/workspaces", HandleAsync).RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUser.GetId(context.User);
        var workspaces = await db.WorkspaceMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == userId)
            .OrderBy(membership => membership.Workspace.CreatedAt)
            .ThenBy(membership => membership.WorkspaceId)
            .Select(membership => new WorkspaceResponse(
                membership.WorkspaceId,
                membership.Workspace.Name,
                membership.Workspace.Slug,
                membership.Role,
                membership.Workspace.CreatedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(workspaces);
    }
}
