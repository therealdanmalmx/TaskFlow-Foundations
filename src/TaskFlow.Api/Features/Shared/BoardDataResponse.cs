namespace TaskFlow.Api.Features.Shared;

public sealed record BoardDataResponse(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string Description,
    string Color,
    bool IsArchived,
    int TaskCount,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TaskResponse> Tasks);
