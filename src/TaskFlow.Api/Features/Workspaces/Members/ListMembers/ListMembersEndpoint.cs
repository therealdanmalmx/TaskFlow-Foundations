using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Features.Shared;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Workspaces.Members.ListMembers;

public static class ListMembersEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/workspaces/{workspaceId:guid}/members", HandleAsync)
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

        var members = await db.WorkspaceMemberships
            .AsNoTracking()
            .Where(membership => membership.WorkspaceId == workspaceId)
            .OrderBy(membership => membership.User.DisplayName)
            .ThenBy(membership => membership.UserId)
            .Select(membership => new MemberResponse(
                membership.UserId,
                membership.User.Username,
                membership.User.Email,
                membership.User.DisplayName,
                membership.Role,
                membership.JoinedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(members);
    }
}
