using TaskFlow.Api.Domain.Workspaces;

namespace TaskFlow.Api.Features.Workspaces.Members.ChangeMemberRole;

public sealed record ChangeMemberRoleRequest(WorkspaceRole? Role);
