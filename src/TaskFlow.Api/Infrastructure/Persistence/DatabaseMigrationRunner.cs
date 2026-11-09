using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TaskFlow.Api.Infrastructure.Persistence;

public static class DatabaseMigrationRunner
{
    private const int LockNamespace = 0x4D494752;
    private const int GlobalKey = 1;

    public static async Task ApplyAsync(
        AppDbContext db,
        CancellationToken cancellationToken = default)
    {
        // Serialize the pending-migration check and every migration transaction across replicas.
        // A dedicated non-pooled session releases the lock on disposal, including failure paths.
        var connectionString = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString())
        {
            Pooling = false,
        };
        await using var connection = new NpgsqlConnection(connectionString.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT pg_advisory_lock($1, $2)", connection);
        command.Parameters.AddWithValue(LockNamespace);
        command.Parameters.AddWithValue(GlobalKey);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await db.Database.MigrateAsync(cancellationToken);
    }
}
