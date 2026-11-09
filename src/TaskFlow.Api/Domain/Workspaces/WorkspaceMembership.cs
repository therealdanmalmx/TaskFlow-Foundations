using TaskFlow.Api.Domain.Users;

namespace TaskFlow.Api.Domain.Workspaces;

public sealed class WorkspaceMembership
{
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public WorkspaceRole Role { get; set; } = WorkspaceRole.Member;
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;

    public Workspace Workspace { get; set; } = null!;
    public User User { get; set; } = null!;
}
