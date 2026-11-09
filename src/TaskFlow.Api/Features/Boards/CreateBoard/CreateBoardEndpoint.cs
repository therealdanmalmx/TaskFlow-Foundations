using TaskFlow.Api.Domain.Boards;
using TaskFlow.Api.Features.Shared;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Features.Boards.CreateBoard;

public static class CreateBoardEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/workspaces/{workspaceId:guid}/boards", HandleAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        Guid workspaceId,
        CreateBoardRequest request,
        HttpContext context,
        AppDbContext db,
        WorkspaceAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUser.GetId(context.User);
        var membership = await authorization.FindMembershipAsync(
            workspaceId,
            userId,
            cancellationToken);
        if (membership is null)
        {
            return Results.NotFound();
        }

        if (!WorkspaceAuthorizationService.CanManage(membership.Role))
        {
            return Results.Forbid();
        }

        var validationError = InputValidation.ValidateBoard(
            request.Name,
            request.Description,
            request.Color);
        if (validationError is not null)
        {
            return Results.BadRequest(new { error = validationError });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await WorkspaceInvariantLock.AcquireAsync(db, workspaceId, cancellationToken);

        membership = await authorization.FindMembershipAsync(
            workspaceId,
            userId,
            cancellationToken);
        if (membership is null)
        {
            return Results.NotFound();
        }

        if (!WorkspaceAuthorizationService.CanManage(membership.Role))
        {
            return Results.Forbid();
        }

        var now = DateTimeOffset.UtcNow;
        var board = new Board
        {
            WorkspaceId = workspaceId,
            Name = request.Name!.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            Color = request.Color!.Trim().ToLowerInvariant(),
            CreatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Boards.Add(board);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var result = new BoardSummaryResponse(
            board.Id,
            board.WorkspaceId,
            board.Name,
            board.Description,
            board.Color,
            board.IsArchived,
            0,
            board.UpdatedAt);
        return Results.Created($"/api/boards/{board.Id}", result);
    }
}
