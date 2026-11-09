namespace TaskFlow.Api.Features.Shared;

public sealed record BoardSummaryResponse(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string Description,
    string Color,
    bool IsArchived,
    int TaskCount,
    DateTimeOffset UpdatedAt);
