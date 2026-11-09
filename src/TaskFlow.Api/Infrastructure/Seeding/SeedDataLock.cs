using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Infrastructure.Seeding;

public static class SeedDataLock
{
    private const int LockNamespace = 0x53454544;
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
