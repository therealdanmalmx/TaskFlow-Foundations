namespace TaskFlow.Api.Infrastructure.Persistence;

public static class BoardInvariantLock
{
    private const int LockNamespace = 0x424F4152;

    public static Task AcquireAsync(
        AppDbContext db,
        Guid boardId,
        CancellationToken cancellationToken = default) =>
        EntityInvariantLock.AcquireAsync(db, LockNamespace, boardId, cancellationToken);

    public static Task AcquireManyAsync(
        AppDbContext db,
        IEnumerable<Guid> boardIds,
        CancellationToken cancellationToken = default) =>
        EntityInvariantLock.AcquireManyAsync(db, LockNamespace, boardIds, cancellationToken);
}
