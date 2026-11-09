using TaskFlow.Api.Domain.Tasks;

namespace TaskFlow.Api.Features.Tasks.UpdateTask;

public sealed record UpdateTaskRequest(
    string? Title,
    string? Description,
    TaskPriority? Priority,
    DateOnly? DueDate,
    Guid? AssigneeId);
