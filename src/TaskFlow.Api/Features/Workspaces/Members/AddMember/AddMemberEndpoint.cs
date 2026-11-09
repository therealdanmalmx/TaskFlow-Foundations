using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaskFlow.Api.Domain.Workspaces;
using TaskFlow.Api.Features.Shared;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Workspaces.Members.AddMember;

public static class AddMemberEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/workspaces/{workspaceId:guid}/members", HandleAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        Guid workspaceId,
        AddMemberRequest request,
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

        if (InputValidation.ValidateUsername(request.Username) is { } usernameError)
        {
            return Results.BadRequest(new { error = usernameError });
        }

        if (request.Role is null || !Enum.IsDefined(request.Role.Value))
        {
            return Results.BadRequest(new { error = "Role must be Owner, Admin, or Member." });
        }

        var normalizedUsername = IdentityNormalizer.Normalize(request.Username!);
        var userPreview = await db.Users
            .AsNoTracking()
            .Where(candidate => candidate.NormalizedUsername == normalizedUsername && candidate.IsActive)
            .Select(candidate => new { candidate.Id })
            .SingleOrDefaultAsync(cancellationToken);
        if (userPreview is null)
        {
            return Results.NotFound(new { error = "User not found." });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Profile changes take the same user-first order before snapshotting workspaces.
        // A membership therefore cannot introduce a task/display-name relationship mid-update.
        await UserInvariantLock.AcquireAsync(db, userPreview.Id, cancellationToken);
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

        if (request.Role == WorkspaceRole.Owner && actor.Role != WorkspaceRole.Owner)
        {
            return Results.Forbid();
        }

        var user = await db.Users.SingleOrDefaultAsync(
            candidate => candidate.Id == userPreview.Id && candidate.IsActive,
            cancellationToken);
        if (user is null)
        {
            return Results.NotFound(new { error = "User not found." });
        }

        if (await db.WorkspaceMemberships.AnyAsync(
                membership => membership.WorkspaceId == workspaceId && membership.UserId == user.Id,
                cancellationToken))
        {
            return Results.Conflict(new { error = "That user is already a workspace member." });
        }

        var membership = new WorkspaceMembership
        {
            WorkspaceId = workspaceId,
            UserId = user.Id,
            Role = request.Role.Value,
            JoinedAt = DateTimeOffset.UtcNow,
        };
        db.WorkspaceMemberships.Add(membership);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "pk_workspace_memberships",
            })
        {
            await transaction.RollbackAsync(cancellationToken);
            return Results.Conflict(new { error = "That user is already a workspace member." });
        }

        return Results.Created(
            $"/api/workspaces/{workspaceId}/members/{user.Id}",
            new MemberResponse(
                user.Id,
                user.Username,
                user.Email,
                user.DisplayName,
                membership.Role,
                membership.JoinedAt));
    }
}
