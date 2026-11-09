using TaskFlow.Api.Domain.Boards;
using TaskFlow.Api.Domain.Tasks;
using TaskFlow.Api.Domain.Workspaces;

namespace TaskFlow.Api.Domain.Users;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Username { get; set; }
    public required string NormalizedUsername { get; set; }
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public required string DisplayName { get; set; }
    public required string PasswordHash { get; set; }
    public bool IsActive { get; set; } = true;
    public int TokenVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<Workspace> CreatedWorkspaces { get; set; } = [];
    public ICollection<WorkspaceMembership> WorkspaceMemberships { get; set; } = [];
    public ICollection<Board> CreatedBoards { get; set; } = [];
    public ICollection<TaskItem> CreatedTasks { get; set; } = [];
    public ICollection<TaskItem> AssignedTasks { get; set; } = [];
}
