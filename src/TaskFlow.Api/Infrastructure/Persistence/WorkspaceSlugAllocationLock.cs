using Microsoft.EntityFrameworkCore;

namespace TaskFlow.Api.Infrastructure.Persistence;

public static class WorkspaceSlugAllocationLock
{
    private const int LockNamespace = 0x534C5547;
    private const int GlobalKey = 1;

    public static async Task AcquireAsync(
        AppDbContext db,
        CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({LockNamespace}, {GlobalKey})",
            cancellationToken);
    }
}
