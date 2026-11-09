using TaskFlow.Api.Domain.Workspaces;
using TaskFlow.Api.Features.Shared;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Persistence;
using TaskFlow.Api.Infrastructure.Text;

namespace TaskFlow.Api.Features.Workspaces.CreateWorkspace;

public static class CreateWorkspaceEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/workspaces", HandleAsync).RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        CreateWorkspaceRequest request,
        HttpContext context,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var validationError = InputValidation.ValidateWorkspaceName(request.Name);
        if (validationError is not null)
        {
            return Results.BadRequest(new { error = validationError });
        }

        var userId = CurrentUser.GetId(context.User);
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await WorkspaceSlugAllocationLock.AcquireAsync(db, cancellationToken);
        var workspace = new Workspace
        {
            Name = request.Name!.Trim(),
            Slug = await SlugGenerator.CreateUniqueAsync(db, request.Name, cancellationToken),
            CreatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync(cancellationToken);

        db.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            WorkspaceId = workspace.Id,
            UserId = userId,
            Role = WorkspaceRole.Owner,
            JoinedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Results.Created(
            $"/api/workspaces/{workspace.Id}",
            new WorkspaceResponse(
                workspace.Id,
                workspace.Name,
                workspace.Slug,
                WorkspaceRole.Owner,
                workspace.CreatedAt));
    }
}
