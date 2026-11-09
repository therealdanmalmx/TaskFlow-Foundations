using TaskFlow.Api.Domain.Boards;
using TaskFlow.Api.Domain.Users;

namespace TaskFlow.Api.Domain.Workspaces;

public sealed class Workspace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public User CreatedByUser { get; set; } = null!;
    public ICollection<WorkspaceMembership> Memberships { get; set; } = [];
    public ICollection<Board> Boards { get; set; } = [];
}
