using TaskFlow.Api.Domain.Workspaces;

namespace TaskFlow.Api.Features.Workspaces.Members.AddMember;

public sealed record AddMemberRequest(string? Username, WorkspaceRole? Role);
