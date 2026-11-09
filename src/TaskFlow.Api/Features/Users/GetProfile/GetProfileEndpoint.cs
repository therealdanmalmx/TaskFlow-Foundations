using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Features.Shared;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Users.GetProfile;

public static class GetProfileEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/users/me", HandleAsync).RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUser.GetId(context.User);
        var user = await db.Users.AsNoTracking().SingleAsync(
            candidate => candidate.Id == userId,
            cancellationToken);
        return Results.Ok(UserResponse.From(user));
    }
}
