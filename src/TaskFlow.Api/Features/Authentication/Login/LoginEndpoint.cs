using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Authentication.Login;

public static class LoginEndpoint
{
    private static readonly string UnknownUserHash =
        PasswordHasher.Hash(Guid.NewGuid().ToString());

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", HandleAsync)
            .RequireRateLimiting(AuthRateLimitPolicies.Login);
    }

    private static async Task<IResult> HandleAsync(
        LoginRequest request,
        AppDbContext db,
        TokenService tokens,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new { error = "Username and password are required." });
        }

        var normalizedUsername = IdentityNormalizer.Normalize(request.Username);
        var userId = await db.Users
            .AsNoTracking()
            .Where(candidate => candidate.NormalizedUsername == normalizedUsername)
            .Select(candidate => (Guid?)candidate.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (userId is null)
        {
            PasswordHasher.Verify(request.Password, UnknownUserHash);
            return InvalidCredentials();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await UserInvariantLock.AcquireAsync(db, userId.Value, cancellationToken);
        var user = await db.Users.SingleOrDefaultAsync(
            candidate => candidate.Id == userId.Value,
            cancellationToken);
        var passwordMatches = PasswordHasher.Verify(
            request.Password,
            user?.PasswordHash ?? UnknownUserHash);

        if (user is null || !user.IsActive || !passwordMatches)
        {
            return InvalidCredentials();
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        // Build the token while the per-user lock still protects its token version.
        var session = tokens.CreateSession(user);
        await transaction.CommitAsync(cancellationToken);
        tokens.SetSessionCookie(response, session);
        return Results.Ok(session);
    }

    private static IResult InvalidCredentials() =>
        Results.Json(
            new { error = "Invalid username or password." },
            statusCode: StatusCodes.Status401Unauthorized);
}
