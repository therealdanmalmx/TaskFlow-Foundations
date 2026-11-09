using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Domain.Workspaces;
using TaskFlow.Api.Features.Shared;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Workspaces.Members.ChangeMemberRole;

public static class ChangeMemberRoleEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/api/workspaces/{workspaceId:guid}/members/{userId:guid}/role", HandleAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        Guid workspaceId,
        Guid userId,
        ChangeMemberRoleRequest request,
        HttpContext context,
        AppDbContext db,
        WorkspaceAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        var actorId = CurrentUser.GetId(context.User);
        var actor = await authorization.FindMembershipAsync(workspaceId, actorId, cancellationToken);
        if (actor is null)
        {
            return Results.NotFound();
        }

        if (!WorkspaceAuthorizationService.CanManage(actor.Role))
        {
            return Results.Forbid();
        }

        if (request.Role is null || !Enum.IsDefined(request.Role.Value))
        {
            return Results.BadRequest(new { error = "Role must be Owner, Admin, or Member." });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await WorkspaceInvariantLock.AcquireAsync(db, workspaceId, cancellationToken);

        actor = await authorization.FindMembershipAsync(workspaceId, actorId, cancellationToken);
        if (actor is null)
        {
            return Results.NotFound();
        }

        if (!WorkspaceAuthorizationService.CanManage(actor.Role))
        {
            return Results.Forbid();
        }

        var target = await db.WorkspaceMemberships
            .Include(membership => membership.User)
            .SingleOrDefaultAsync(
                membership => membership.WorkspaceId == workspaceId && membership.UserId == userId,
                cancellationToken);
        if (target is null)
        {
            return Results.NotFound();
        }

        if (actor.Role == WorkspaceRole.Admin &&
            (target.Role == WorkspaceRole.Owner || request.Role == WorkspaceRole.Owner))
        {
            return Results.Forbid();
        }

        if (target.Role == WorkspaceRole.Owner && request.Role != WorkspaceRole.Owner)
        {
            var ownerCount = await db.WorkspaceMemberships.CountAsync(
                membership => membership.WorkspaceId == workspaceId && membership.Role == WorkspaceRole.Owner,
                cancellationToken);
            if (ownerCount == 1)
            {
                return Results.Conflict(new { error = "A workspace must always have at least one owner." });
            }
        }

        target.Role = request.Role.Value;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new MemberResponse(
            target.UserId,
            target.User.Username,
            target.User.Email,
            target.User.DisplayName,
            target.Role,
            target.JoinedAt));
    }
}
