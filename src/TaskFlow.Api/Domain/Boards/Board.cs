using TaskFlow.Api.Domain.Tasks;
using TaskFlow.Api.Domain.Users;
using TaskFlow.Api.Domain.Workspaces;

namespace TaskFlow.Api.Domain.Boards;

public sealed class Board
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
    public required string Color { get; set; }
    public bool IsArchived { get; set; }
    public long Revision { get; set; } = 1;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Workspace Workspace { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public ICollection<TaskItem> Tasks { get; set; } = [];

    public void MarkChanged(DateTimeOffset changedAt)
    {
        Revision = checked(Revision + 1);
        if (changedAt > UpdatedAt)
        {
            UpdatedAt = changedAt;
        }
    }
}
