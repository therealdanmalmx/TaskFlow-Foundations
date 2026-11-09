using TaskFlow.Api.Domain.Tasks;

namespace TaskFlow.Api.Features.Shared;

public sealed record TaskResponse(
    Guid Id,
    Guid BoardId,
    string Title,
    string Description,
    TaskState Status,
    TaskPriority Priority,
    DateOnly? DueDate,
    int Position,
    Guid CreatedByUserId,
    string CreatedByName,
    Guid? AssigneeId,
    string? AssigneeName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);
