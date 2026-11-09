using TaskFlow.Api.Domain.Tasks;

namespace TaskFlow.Api.Features.Tasks.UpdateTaskStatus;

public sealed record UpdateTaskStatusRequest(
    TaskState? Status,
    string? Placement = null,
    Guid? BeforeTaskId = null);
