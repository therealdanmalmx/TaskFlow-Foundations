using TaskFlow.Api.Domain.Workspaces;

namespace TaskFlow.Api.Features.Shared;

public sealed record BoardDetailResponse(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string Description,
    string Color,
    bool IsArchived,
    int TaskCount,
    DateTimeOffset UpdatedAt,
    WorkspaceRole Role,
    IReadOnlyList<TaskResponse> Tasks)
{
    public static BoardDetailResponse From(BoardDataResponse board, WorkspaceRole role) =>
        new(
            board.Id,
            board.WorkspaceId,
            board.Name,
            board.Description,
            board.Color,
            board.IsArchived,
            board.TaskCount,
            board.UpdatedAt,
            role,
            board.Tasks);
}
