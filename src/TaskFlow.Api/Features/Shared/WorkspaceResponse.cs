using TaskFlow.Api.Domain.Workspaces;

namespace TaskFlow.Api.Features.Shared;

public sealed record WorkspaceResponse(
    Guid Id,
    string Name,
    string Slug,
    WorkspaceRole Role,
    DateTimeOffset CreatedAt);
