using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace TaskFlow.Api.Infrastructure.Persistence;

public static class EntityInvariantLock
{
    public static long GetKey(int lockNamespace, Guid resourceId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{lockNamespace:X8}:{resourceId:D}"));
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }

    public static Task AcquireAsync(
        AppDbContext db,
        int lockNamespace,
        Guid resourceId,
        CancellationToken cancellationToken = default) =>
        AcquireKeyAsync(db, GetKey(lockNamespace, resourceId), cancellationToken);

    public static async Task AcquireManyAsync(
        AppDbContext db,
        int lockNamespace,
        IEnumerable<Guid> resourceIds,
        CancellationToken cancellationToken = default)
    {
        // Order the actual lock keys so even a hash collision cannot reverse lock order.
        foreach (var key in resourceIds.Select(id => GetKey(lockNamespace, id)).Distinct().Order())
        {
            await AcquireKeyAsync(db, key, cancellationToken);
        }
    }

    private static Task AcquireKeyAsync(
        AppDbContext db,
        long key,
        CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({key})",
            cancellationToken);
}
