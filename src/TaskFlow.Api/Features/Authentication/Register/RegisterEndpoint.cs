using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Domain.Boards;
using TaskFlow.Api.Domain.Users;
using TaskFlow.Api.Domain.Workspaces;
using TaskFlow.Api.Features.Shared;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Persistence;
using TaskFlow.Api.Infrastructure.Text;

namespace TaskFlow.Api.Features.Authentication.Register;

public static class RegisterEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/register", HandleAsync)
            .RequireRateLimiting(AuthRateLimitPolicies.Registration);
    }

    private static async Task<IResult> HandleAsync(
        RegisterRequest request,
        AppDbContext db,
        TokenService tokens,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var validationError = InputValidation.ValidateUsername(request.Username)
            ?? InputValidation.ValidateEmail(request.Email)
            ?? InputValidation.ValidateDisplayName(request.DisplayName)
            ?? InputValidation.ValidatePassword(request.Password);
        if (validationError is not null)
        {
            return Results.BadRequest(new { error = validationError });
        }

        var username = request.Username!.Trim();
        var email = request.Email!.Trim();
        var displayName = request.DisplayName!.Trim();
        var normalizedUsername = IdentityNormalizer.Normalize(username);
        var normalizedEmail = IdentityNormalizer.Normalize(email);

        if (await db.Users.AnyAsync(
                user => user.NormalizedUsername == normalizedUsername || user.NormalizedEmail == normalizedEmail,
                cancellationToken))
        {
            return Results.Conflict(new { error = "That username or email address is already registered." });
        }

        var passwordHash = PasswordHasher.Hash(request.Password!);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await WorkspaceSlugAllocationLock.AcquireAsync(db, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var user = new User
            {
                Username = username,
                NormalizedUsername = normalizedUsername,
                Email = email,
                NormalizedEmail = normalizedEmail,
                DisplayName = displayName,
                PasswordHash = passwordHash,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);

            var workspaceName = displayName.Length <= 88
                ? $"{displayName}'s Workspace"
                : $"{displayName[..88]}'s Workspace";
            var workspace = new Workspace
            {
                Name = workspaceName,
                Slug = await SlugGenerator.CreateUniqueAsync(db, username, cancellationToken),
                CreatedByUserId = user.Id,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Workspaces.Add(workspace);
            await db.SaveChangesAsync(cancellationToken);

            db.WorkspaceMemberships.Add(new WorkspaceMembership
            {
                WorkspaceId = workspace.Id,
                UserId = user.Id,
                Role = WorkspaceRole.Owner,
                JoinedAt = now,
            });
            db.Boards.Add(new Board
            {
                WorkspaceId = workspace.Id,
                Name = "My tasks",
                Description = "A place to plan what comes next.",
                Color = InputValidation.DefaultBoardColor,
                CreatedByUserId = user.Id,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var session = tokens.CreateSession(user);
            tokens.SetSessionCookie(response, session);
            return Results.Created("/api/users/me", session);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Results.Conflict(new { error = "That username or email address is already registered." });
        }
    }
}
