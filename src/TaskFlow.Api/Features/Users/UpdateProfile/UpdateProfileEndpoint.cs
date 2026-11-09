using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Features.Shared;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Caching;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Users.UpdateProfile;

public static class UpdateProfileEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/api/users/me", HandleAsync).RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        UpdateProfileRequest request,
        HttpContext context,
        AppDbContext db,
        BoardCache cache,
        CancellationToken cancellationToken)
    {
        var validationError = InputValidation.ValidateDisplayName(request.DisplayName)
            ?? InputValidation.ValidateEmail(request.Email);
        if (validationError is not null)
        {
            return Results.BadRequest(new { error = validationError });
        }

        var userId = CurrentUser.GetId(context.User);
        var normalizedEmail = IdentityNormalizer.Normalize(request.Email!);
        if (await db.Users.AnyAsync(
                user => user.Id != userId && user.NormalizedEmail == normalizedEmail,
                cancellationToken))
        {
            return Results.Conflict(new { error = "That email address is already registered." });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await UserInvariantLock.AcquireAsync(db, userId, cancellationToken);
        var user = await db.Users.SingleAsync(candidate => candidate.Id == userId, cancellationToken);

        // A display name is embedded in cached task projections. Lock every workspace
        // where this user can already affect a task before discovering the exact boards.
        // Acquiring all workspace locks before any board lock preserves the global order.
        var workspaceIds = await db.Workspaces
            .Where(workspace =>
                workspace.Memberships.Any(membership => membership.UserId == userId) ||
                workspace.Boards.Any(board => board.Tasks.Any(
                    task => task.CreatedByUserId == userId || task.AssignedToUserId == userId)))
            .Select(workspace => workspace.Id)
            .OrderBy(workspaceId => workspaceId)
            .ToListAsync(cancellationToken);
        await WorkspaceInvariantLock.AcquireManyAsync(db, workspaceIds, cancellationToken);

        var boardIds = await db.Tasks
            .Where(task => task.CreatedByUserId == userId || task.AssignedToUserId == userId)
            .Select(task => task.BoardId)
            .Distinct()
            .OrderBy(boardId => boardId)
            .ToListAsync(cancellationToken);
        await BoardInvariantLock.AcquireManyAsync(db, boardIds, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        user.DisplayName = request.DisplayName!.Trim();
        user.Email = request.Email!.Trim();
        user.NormalizedEmail = normalizedEmail;
        user.UpdatedAt = now;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await db.Boards
                .Where(board => boardIds.Contains(board.Id))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(board => board.Revision, board => board.Revision + 1)
                        .SetProperty(
                            board => board.UpdatedAt,
                            board => board.UpdatedAt < now ? now : board.UpdatedAt),
                    cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { error = "That email address is already registered." });
        }

        foreach (var boardId in boardIds)
        {
            await cache.InvalidateAsync(boardId);
        }

        return Results.Ok(UserResponse.From(user));
    }
}
