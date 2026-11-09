using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Domain.Workspaces;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Infrastructure.Authentication;

public sealed class WorkspaceAuthorizationService(AppDbContext db)
{
    public Task<WorkspaceMembership?> FindMembershipAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        db.WorkspaceMemberships.AsNoTracking().SingleOrDefaultAsync(
            membership => membership.WorkspaceId == workspaceId && membership.UserId == userId,
            cancellationToken);

    public static bool CanManage(WorkspaceRole role) =>
        role is WorkspaceRole.Owner or WorkspaceRole.Admin;
}
