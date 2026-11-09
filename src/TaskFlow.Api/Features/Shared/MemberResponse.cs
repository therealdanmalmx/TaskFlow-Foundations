using TaskFlow.Api.Domain.Workspaces;

namespace TaskFlow.Api.Features.Shared;

public sealed record MemberResponse(
    Guid UserId,
    string Username,
    string Email,
    string DisplayName,
    WorkspaceRole Role,
    DateTimeOffset JoinedAt);
