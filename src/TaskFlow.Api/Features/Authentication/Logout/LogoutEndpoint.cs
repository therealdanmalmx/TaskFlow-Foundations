using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Authentication.Logout;

public static class LogoutEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/logout", HandleAsync).RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        AppDbContext db,
        TokenService tokens,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUser.GetId(context.User);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await UserInvariantLock.AcquireAsync(db, userId, cancellationToken);
        var user = await db.Users.SingleOrDefaultAsync(
            candidate => candidate.Id == userId,
            cancellationToken);
        if (user is not null)
        {
            user.TokenVersion++;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        tokens.ClearSessionCookie(context.Response);
        return Results.NoContent();
    }
}
