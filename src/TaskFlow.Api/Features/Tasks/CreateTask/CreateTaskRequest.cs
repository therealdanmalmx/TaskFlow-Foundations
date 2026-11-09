using TaskFlow.Api.Domain.Tasks;

namespace TaskFlow.Api.Features.Tasks.CreateTask;

public sealed record CreateTaskRequest(
    string? Title,
    string? Description = null,
    TaskPriority? Priority = null,
    DateOnly? DueDate = null,
    Guid? AssigneeId = null);
