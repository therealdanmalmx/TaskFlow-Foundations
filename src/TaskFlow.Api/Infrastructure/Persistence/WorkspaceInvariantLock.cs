namespace TaskFlow.Api.Infrastructure.Persistence;

public static class WorkspaceInvariantLock
{
    private const int LockNamespace = 0x54534B46;

    public static Task AcquireAsync(
        AppDbContext db,
        Guid workspaceId,
        CancellationToken cancellationToken = default) =>
        EntityInvariantLock.AcquireAsync(db, LockNamespace, workspaceId, cancellationToken);

    public static Task AcquireManyAsync(
        AppDbContext db,
        IEnumerable<Guid> workspaceIds,
        CancellationToken cancellationToken = default) =>
        EntityInvariantLock.AcquireManyAsync(db, LockNamespace, workspaceIds, cancellationToken);
}
