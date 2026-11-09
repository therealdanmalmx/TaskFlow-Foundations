namespace TaskFlow.Api.Infrastructure.Persistence;

public static class UserInvariantLock
{
    private const int LockNamespace = 0x55534552;

    public static Task AcquireAsync(
        AppDbContext db,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        EntityInvariantLock.AcquireAsync(db, LockNamespace, userId, cancellationToken);
}
