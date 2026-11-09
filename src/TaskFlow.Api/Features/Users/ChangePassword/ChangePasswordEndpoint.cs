using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Features.Shared;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Users.ChangePassword;

public static class ChangePasswordEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/api/users/me/password", HandleAsync)
            .RequireAuthorization()
            .RequireRateLimiting(AuthRateLimitPolicies.PasswordChange);
    }

    private static async Task<IResult> HandleAsync(
        ChangePasswordRequest request,
        HttpContext context,
        AppDbContext db,
        TokenService tokens,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            return Results.BadRequest(new { error = "Current password is required." });
        }

        var validationError = InputValidation.ValidatePassword(request.NewPassword);
        if (validationError is not null)
        {
            return Results.BadRequest(new { error = validationError });
        }

        var userId = CurrentUser.GetId(context.User);
        var newPasswordHash = PasswordHasher.Hash(request.NewPassword!);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await UserInvariantLock.AcquireAsync(db, userId, cancellationToken);
        var user = await db.Users.SingleAsync(candidate => candidate.Id == userId, cancellationToken);
        if (!PasswordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return Results.BadRequest(new { error = "Current password is incorrect." });
        }

        user.PasswordHash = newPasswordHash;
        user.TokenVersion++;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var session = tokens.CreateSession(user);
        tokens.SetSessionCookie(context.Response, session);
        return Results.Ok(session);
    }
}
