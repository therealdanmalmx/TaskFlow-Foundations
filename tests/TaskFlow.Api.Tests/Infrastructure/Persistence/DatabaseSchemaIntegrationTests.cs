using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TaskFlow.Api.Infrastructure.Persistence;
using TaskFlow.Api.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Infrastructure.Persistence;

[Collection(TaskFlowApiCollection.Name)]
public sealed class DatabaseSchemaIntegrationTests(
    TaskFlowTestEnvironment environment,
    ITestOutputHelper output)
    : IntegrationTestBase(environment, output, startFromEmptyDatabase: true)
{
    [Fact]
    public async Task Migrations_build_the_expected_tables_columns_and_migration_history()
    {
        await using var connection = await OpenDatabaseConnectionAsync();

        var tables = await ReadPublicTablesAsync(connection);
        Assert.Equal(
            ["__EFMigrationsHistory", "boards", "tasks", "users", "workspace_memberships", "workspaces"],
            tables.Order(StringComparer.Ordinal));

        await using (var historyCommand = connection.CreateCommand())
        {
            historyCommand.CommandText =
                "SELECT migration_id FROM public.\"__EFMigrationsHistory\" ORDER BY migration_id;";
            await using var reader = await historyCommand.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("20260829184834_InitialCreate", reader.GetString(0));
            Assert.False(await reader.ReadAsync());
        }

        var columns = await ReadColumnsAsync(connection);

        AssertTableColumns(
            columns,
            "__EFMigrationsHistory",
            "migration_id",
            "product_version");
        AssertColumn(columns, "__EFMigrationsHistory", "migration_id", "character varying", false, 150);
        AssertColumn(columns, "__EFMigrationsHistory", "product_version", "character varying", false, 32);

        AssertTableColumns(
            columns,
            "users",
            "id",
            "username",
            "normalized_username",
            "email",
            "normalized_email",
            "display_name",
            "password_hash",
            "is_active",
            "token_version",
            "created_at",
            "updated_at",
            "last_login_at");
        AssertColumn(columns, "users", "id", "uuid", false);
        AssertColumn(columns, "users", "username", "character varying", false, 50);
        AssertColumn(columns, "users", "normalized_username", "character varying", false, 50);
        AssertColumn(columns, "users", "email", "character varying", false, 254);
        AssertColumn(columns, "users", "normalized_email", "character varying", false, 254);
        AssertColumn(columns, "users", "display_name", "character varying", false, 100);
        AssertColumn(columns, "users", "password_hash", "character varying", false, 500);
        AssertColumn(columns, "users", "is_active", "boolean", false, defaultValue: "true");
        AssertColumn(columns, "users", "token_version", "integer", false, defaultValue: "0");
        AssertColumn(columns, "users", "created_at", "timestamp with time zone", false);
        AssertColumn(columns, "users", "updated_at", "timestamp with time zone", false);
        AssertColumn(columns, "users", "last_login_at", "timestamp with time zone", true);

        AssertTableColumns(
            columns,
            "workspaces",
            "id",
            "name",
            "slug",
            "created_by_user_id",
            "created_at",
            "updated_at");
        AssertColumn(columns, "workspaces", "id", "uuid", false);
        AssertColumn(columns, "workspaces", "name", "character varying", false, 100);
        AssertColumn(columns, "workspaces", "slug", "character varying", false, 100);
        AssertColumn(columns, "workspaces", "created_by_user_id", "uuid", false);
        AssertColumn(columns, "workspaces", "created_at", "timestamp with time zone", false);
        AssertColumn(columns, "workspaces", "updated_at", "timestamp with time zone", false);

        AssertTableColumns(
            columns,
            "workspace_memberships",
            "workspace_id",
            "user_id",
            "role",
            "joined_at");
        AssertColumn(columns, "workspace_memberships", "workspace_id", "uuid", false);
        AssertColumn(columns, "workspace_memberships", "user_id", "uuid", false);
        AssertColumn(columns, "workspace_memberships", "role", "character varying", false, 10);
        AssertColumn(columns, "workspace_memberships", "joined_at", "timestamp with time zone", false);

        AssertTableColumns(
            columns,
            "boards",
            "id",
            "workspace_id",
            "name",
            "description",
            "color",
            "is_archived",
            "revision",
            "created_by_user_id",
            "created_at",
            "updated_at");
        AssertColumn(columns, "boards", "id", "uuid", false);
        AssertColumn(columns, "boards", "workspace_id", "uuid", false);
        AssertColumn(columns, "boards", "name", "character varying", false, 100);
        AssertColumn(columns, "boards", "description", "character varying", false, 2_000);
        AssertColumn(columns, "boards", "color", "character varying", false, 20);
        AssertColumn(columns, "boards", "is_archived", "boolean", false);
        AssertColumn(columns, "boards", "revision", "bigint", false, defaultValue: "1");
        AssertColumn(columns, "boards", "created_by_user_id", "uuid", false);
        AssertColumn(columns, "boards", "created_at", "timestamp with time zone", false);
        AssertColumn(columns, "boards", "updated_at", "timestamp with time zone", false);

        AssertTableColumns(
            columns,
            "tasks",
            "id",
            "board_id",
            "title",
            "description",
            "status",
            "priority",
            "due_date",
            "position",
            "created_by_user_id",
            "assigned_to_user_id",
            "created_at",
            "updated_at",
            "completed_at");
        AssertColumn(columns, "tasks", "id", "uuid", false);
        AssertColumn(columns, "tasks", "board_id", "uuid", false);
        AssertColumn(columns, "tasks", "title", "character varying", false, 200);
        AssertColumn(columns, "tasks", "description", "character varying", false, 4_000);
        AssertColumn(columns, "tasks", "status", "character varying", false, 10);
        AssertColumn(columns, "tasks", "priority", "character varying", false, 10);
        AssertColumn(columns, "tasks", "due_date", "date", true);
        AssertColumn(columns, "tasks", "position", "integer", false, defaultValue: "0");
        AssertColumn(columns, "tasks", "created_by_user_id", "uuid", false);
        AssertColumn(columns, "tasks", "assigned_to_user_id", "uuid", true);
        AssertColumn(columns, "tasks", "created_at", "timestamp with time zone", false);
        AssertColumn(columns, "tasks", "updated_at", "timestamp with time zone", false);
        AssertColumn(columns, "tasks", "completed_at", "timestamp with time zone", true);
    }

    [Fact]
    public async Task Migrations_build_the_expected_keys_indexes_and_foreign_key_actions()
    {
        await using var connection = await OpenDatabaseConnectionAsync();

        var indexes = await ReadIndexesAsync(connection);
        Assert.Equal(17, indexes.Count);

        AssertIndex(indexes, "users", "pk_users", true, true, "id");
        AssertIndex(indexes, "users", "ix_users_normalized_email", false, true, "normalized_email");
        AssertIndex(indexes, "users", "ix_users_normalized_username", false, true, "normalized_username");

        AssertIndex(indexes, "workspaces", "pk_workspaces", true, true, "id");
        AssertIndex(indexes, "workspaces", "ix_workspaces_slug", false, true, "slug");
        AssertIndex(indexes, "workspaces", "ix_workspaces_created_by_user_id", false, false, "created_by_user_id");

        AssertIndex(indexes, "workspace_memberships", "pk_workspace_memberships", true, true, "workspace_id", "user_id");
        AssertIndex(indexes, "workspace_memberships", "ix_workspace_memberships_user_id", false, false, "user_id");
        AssertIndex(
            indexes,
            "workspace_memberships",
            "ix_workspace_memberships_workspace_id_role",
            false,
            false,
            "workspace_id",
            "role");

        AssertIndex(indexes, "boards", "pk_boards", true, true, "id");
        AssertIndex(indexes, "boards", "ix_boards_created_by_user_id", false, false, "created_by_user_id");
        AssertIndex(
            indexes,
            "boards",
            "ix_boards_workspace_id_is_archived",
            false,
            false,
            "workspace_id",
            "is_archived");

        AssertIndex(indexes, "tasks", "pk_tasks", true, true, "id");
        AssertIndex(indexes, "tasks", "ix_tasks_assigned_to_user_id", false, false, "assigned_to_user_id");
        AssertIndex(indexes, "tasks", "ix_tasks_board_id_due_date", false, false, "board_id", "due_date");
        AssertIndex(
            indexes,
            "tasks",
            "ix_tasks_board_id_status_position",
            false,
            false,
            "board_id",
            "status",
            "position");
        AssertIndex(indexes, "tasks", "ix_tasks_created_by_user_id", false, false, "created_by_user_id");

        var foreignKeys = await ReadForeignKeysAsync(connection);
        Assert.Equal(8, foreignKeys.Count);
        AssertForeignKey(
            foreignKeys,
            "fk_workspaces_users_created_by_user_id",
            "workspaces",
            "users",
            "RESTRICT");
        AssertForeignKey(
            foreignKeys,
            "fk_workspace_memberships_workspaces_workspace_id",
            "workspace_memberships",
            "workspaces",
            "CASCADE");
        AssertForeignKey(
            foreignKeys,
            "fk_workspace_memberships_users_user_id",
            "workspace_memberships",
            "users",
            "RESTRICT");
        AssertForeignKey(
            foreignKeys,
            "fk_boards_workspaces_workspace_id",
            "boards",
            "workspaces",
            "CASCADE");
        AssertForeignKey(
            foreignKeys,
            "fk_boards_users_created_by_user_id",
            "boards",
            "users",
            "RESTRICT");
        AssertForeignKey(foreignKeys, "fk_tasks_boards_board_id", "tasks", "boards", "CASCADE");
        AssertForeignKey(
            foreignKeys,
            "fk_tasks_users_created_by_user_id",
            "tasks",
            "users",
            "RESTRICT");
        AssertForeignKey(
            foreignKeys,
            "fk_tasks_users_assigned_to_user_id",
            "tasks",
            "users",
            "SET NULL");
    }

    [Fact]
    public async Task Normalized_identity_and_workspace_membership_uniqueness_are_enforced_by_PostgreSQL()
    {
        await using var connection = await OpenDatabaseConnectionAsync();
        var unique = Guid.NewGuid().ToString("N");
        var normalizedUsername = $"SCHEMA-USER-{unique}";
        var normalizedEmail = $"SCHEMA-{unique}@EXAMPLE.TEST";

        await InsertUserAsync(
            connection,
            $"SchemaUser-{unique}",
            normalizedUsername,
            $"schema-{unique}@example.test",
            normalizedEmail);

        var usernameViolation = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await InsertUserAsync(
                connection,
                $"DifferentUser-{unique}",
                normalizedUsername,
                $"different-{unique}@example.test",
                $"DIFFERENT-{unique}@EXAMPLE.TEST");
        });
        Assert.Equal(PostgresErrorCodes.UniqueViolation, usernameViolation.SqlState);
        Assert.Equal("ix_users_normalized_username", usernameViolation.ConstraintName);

        var emailViolation = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await InsertUserAsync(
                connection,
                $"AnotherUser-{unique}",
                $"ANOTHER-USER-{unique}",
                $"another-{unique}@example.test",
                normalizedEmail);
        });
        Assert.Equal(PostgresErrorCodes.UniqueViolation, emailViolation.SqlState);
        Assert.Equal("ix_users_normalized_email", emailViolation.ConstraintName);

        await using var duplicateMembership = connection.CreateCommand();
        duplicateMembership.CommandText =
            """
            INSERT INTO workspace_memberships (workspace_id, user_id, role, joined_at)
            SELECT workspace_id, user_id, 'Member', now()
            FROM workspace_memberships
            LIMIT 1;
            """;
        var membershipViolation = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await duplicateMembership.ExecuteNonQueryAsync();
        });
        Assert.Equal(PostgresErrorCodes.UniqueViolation, membershipViolation.SqlState);
        Assert.Equal("pk_workspace_memberships", membershipViolation.ConstraintName);
    }

    [Fact]
    public async Task Delete_actions_cascade_tasks_restrict_creators_and_clear_assignees()
    {
        await using var connection = await OpenDatabaseConnectionAsync();
        var (adminUserId, workspaceId, boardId) = await ReadSeedIdsAsync(connection);
        var unique = Guid.NewGuid().ToString("N");

        var cascadeBoardId = await InsertBoardAsync(
            connection,
            workspaceId,
            adminUserId,
            $"Cascade probe {unique}");
        var cascadingTaskId = await InsertTaskAsync(
            connection,
            cascadeBoardId,
            adminUserId,
            assignedToUserId: null,
            $"Cascade probe {unique}");

        await ExecuteDeleteAsync(connection, "boards", cascadeBoardId);
        Assert.False(await RowExistsAsync(connection, "tasks", cascadingTaskId));

        var creatorId = await InsertUserAsync(
            connection,
            $"Creator-{unique}",
            $"CREATOR-{unique}",
            $"creator-{unique}@example.test",
            $"CREATOR-{unique}@EXAMPLE.TEST");
        await InsertWorkspaceAsync(connection, creatorId, $"creator-workspace-{unique}");

        var restrictViolation = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await ExecuteDeleteAsync(connection, "users", creatorId);
        });
        Assert.Equal(PostgresErrorCodes.RestrictViolation, restrictViolation.SqlState);
        Assert.Equal("fk_workspaces_users_created_by_user_id", restrictViolation.ConstraintName);
        Assert.True(await RowExistsAsync(connection, "users", creatorId));

        var assigneeId = await InsertUserAsync(
            connection,
            $"Assignee-{unique}",
            $"ASSIGNEE-{unique}",
            $"assignee-{unique}@example.test",
            $"ASSIGNEE-{unique}@EXAMPLE.TEST");
        var assignedTaskId = await InsertTaskAsync(
            connection,
            boardId,
            adminUserId,
            assigneeId,
            $"Set-null probe {unique}");

        await ExecuteDeleteAsync(connection, "users", assigneeId);
        Assert.True(await RowExistsAsync(connection, "tasks", assignedTaskId));
        Assert.Null(await ReadTaskAssigneeAsync(connection, assignedTaskId));
    }

    private async Task<NpgsqlConnection> OpenDatabaseConnectionAsync()
    {
        using var scope = Application.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connectionString = db.Database.GetConnectionString();
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<string[]> ReadPublicTablesAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_type = 'BASE TABLE';
            """;

        List<string> tables = [];
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return [.. tables];
    }

    private static async Task<Dictionary<(string Table, string Column), (
        string DataType,
        bool Nullable,
        int? MaxLength,
        string? DefaultValue,
        string? IdentityGeneration)>> ReadColumnsAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                table_name,
                column_name,
                data_type,
                is_nullable = 'YES',
                character_maximum_length,
                column_default,
                identity_generation
            FROM information_schema.columns
            WHERE table_schema = 'public';
            """;

        Dictionary<(string Table, string Column), (
            string DataType,
            bool Nullable,
            int? MaxLength,
            string? DefaultValue,
            string? IdentityGeneration)> columns = [];
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(
                (reader.GetString(0), reader.GetString(1)),
                (
                    reader.GetString(2),
                    reader.GetBoolean(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return columns;
    }

    private static async Task<Dictionary<(string Table, string Name), (
        bool Primary,
        bool Unique,
        string[] Columns)>> ReadIndexesAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                table_relation.relname,
                index_relation.relname,
                index_metadata.indisprimary,
                index_metadata.indisunique,
                array_agg(column_metadata.attname ORDER BY indexed_column.ordinality)::text[]
            FROM pg_catalog.pg_index AS index_metadata
            INNER JOIN pg_catalog.pg_class AS table_relation
                ON table_relation.oid = index_metadata.indrelid
            INNER JOIN pg_catalog.pg_namespace AS table_namespace
                ON table_namespace.oid = table_relation.relnamespace
            INNER JOIN pg_catalog.pg_class AS index_relation
                ON index_relation.oid = index_metadata.indexrelid
            CROSS JOIN LATERAL unnest(index_metadata.indkey)
                WITH ORDINALITY AS indexed_column(attribute_number, ordinality)
            INNER JOIN pg_catalog.pg_attribute AS column_metadata
                ON column_metadata.attrelid = table_relation.oid
               AND column_metadata.attnum = indexed_column.attribute_number
            WHERE table_namespace.nspname = 'public'
              AND table_relation.relname IN (
                  'users',
                  'workspaces',
                  'workspace_memberships',
                  'boards',
                  'tasks')
            GROUP BY
                table_relation.relname,
                index_relation.relname,
                index_metadata.indisprimary,
                index_metadata.indisunique;
            """;

        Dictionary<(string Table, string Name), (
            bool Primary,
            bool Unique,
            string[] Columns)> indexes = [];
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            indexes.Add(
                (reader.GetString(0), reader.GetString(1)),
                (reader.GetBoolean(2), reader.GetBoolean(3), reader.GetFieldValue<string[]>(4)));
        }

        return indexes;
    }

    private static async Task<Dictionary<string, (
        string DependentTable,
        string PrincipalTable,
        string DeleteAction)>> ReadForeignKeysAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                constraint_metadata.conname,
                dependent_table.relname,
                principal_table.relname,
                CASE constraint_metadata.confdeltype
                    WHEN 'c' THEN 'CASCADE'
                    WHEN 'n' THEN 'SET NULL'
                    WHEN 'r' THEN 'RESTRICT'
                    WHEN 'a' THEN 'NO ACTION'
                    WHEN 'd' THEN 'SET DEFAULT'
                END
            FROM pg_catalog.pg_constraint AS constraint_metadata
            INNER JOIN pg_catalog.pg_namespace AS constraint_namespace
                ON constraint_namespace.oid = constraint_metadata.connamespace
            INNER JOIN pg_catalog.pg_class AS dependent_table
                ON dependent_table.oid = constraint_metadata.conrelid
            INNER JOIN pg_catalog.pg_class AS principal_table
                ON principal_table.oid = constraint_metadata.confrelid
            WHERE constraint_namespace.nspname = 'public'
              AND constraint_metadata.contype = 'f';
            """;

        Dictionary<string, (
            string DependentTable,
            string PrincipalTable,
            string DeleteAction)> foreignKeys = [];
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            foreignKeys.Add(
                reader.GetString(0),
                (reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return foreignKeys;
    }

    private static void AssertTableColumns(
        IReadOnlyDictionary<(string Table, string Column), (
            string DataType,
            bool Nullable,
            int? MaxLength,
            string? DefaultValue,
            string? IdentityGeneration)> columns,
        string table,
        params string[] expectedColumns)
    {
        var actualColumns = columns.Keys
            .Where(key => key.Table == table)
            .Select(key => key.Column)
            .Order(StringComparer.Ordinal);
        Assert.Equal(expectedColumns.Order(StringComparer.Ordinal), actualColumns);
    }

    private static void AssertColumn(
        IReadOnlyDictionary<(string Table, string Column), (
            string DataType,
            bool Nullable,
            int? MaxLength,
            string? DefaultValue,
            string? IdentityGeneration)> columns,
        string table,
        string column,
        string dataType,
        bool nullable,
        int? maxLength = null,
        string? defaultValue = null,
        string? identityGeneration = null)
    {
        Assert.True(columns.TryGetValue((table, column), out var actual), $"Missing {table}.{column}.");
        Assert.Equal(dataType, actual.DataType);
        Assert.Equal(nullable, actual.Nullable);
        Assert.Equal(maxLength, actual.MaxLength);
        Assert.Equal(defaultValue, actual.DefaultValue);
        Assert.Equal(identityGeneration, actual.IdentityGeneration);
    }

    private static void AssertIndex(
        IReadOnlyDictionary<(string Table, string Name), (
            bool Primary,
            bool Unique,
            string[] Columns)> indexes,
        string table,
        string name,
        bool primary,
        bool unique,
        params string[] columns)
    {
        Assert.True(indexes.TryGetValue((table, name), out var actual), $"Missing index {name} on {table}.");
        Assert.Equal(primary, actual.Primary);
        Assert.Equal(unique, actual.Unique);
        Assert.Equal(columns, actual.Columns);
    }

    private static void AssertForeignKey(
        IReadOnlyDictionary<string, (
            string DependentTable,
            string PrincipalTable,
            string DeleteAction)> foreignKeys,
        string name,
        string dependentTable,
        string principalTable,
        string deleteAction)
    {
        Assert.True(foreignKeys.TryGetValue(name, out var actual), $"Missing foreign key {name}.");
        Assert.Equal(dependentTable, actual.DependentTable);
        Assert.Equal(principalTable, actual.PrincipalTable);
        Assert.Equal(deleteAction, actual.DeleteAction);
    }

    private static async Task<Guid> InsertUserAsync(
        NpgsqlConnection connection,
        string username,
        string normalizedUsername,
        string email,
        string normalizedEmail)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO users (
                id,
                username,
                normalized_username,
                email,
                normalized_email,
                display_name,
                password_hash,
                created_at,
                updated_at)
            VALUES (
                gen_random_uuid(),
                @username,
                @normalized_username,
                @email,
                @normalized_email,
                @display_name,
                @password_hash,
                now(),
                now())
            RETURNING id;
            """;
        command.Parameters.AddWithValue("username", username);
        command.Parameters.AddWithValue("normalized_username", normalizedUsername);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("normalized_email", normalizedEmail);
        command.Parameters.AddWithValue("display_name", username);
        command.Parameters.AddWithValue("password_hash", "schema-test-password-hash");
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<Guid> InsertWorkspaceAsync(
        NpgsqlConnection connection,
        Guid creatorId,
        string slug)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO workspaces (id, name, slug, created_by_user_id, created_at, updated_at)
            VALUES (gen_random_uuid(), @name, @slug, @creator_id, now(), now())
            RETURNING id;
            """;
        command.Parameters.AddWithValue("name", $"Workspace {slug}");
        command.Parameters.AddWithValue("slug", slug);
        command.Parameters.AddWithValue("creator_id", creatorId);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<Guid> InsertBoardAsync(
        NpgsqlConnection connection,
        Guid workspaceId,
        Guid creatorId,
        string name)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO boards (
                id,
                workspace_id,
                name,
                description,
                color,
                is_archived,
                created_by_user_id,
                created_at,
                updated_at)
            VALUES (
                gen_random_uuid(),
                @workspace_id,
                @name,
                '',
                '#2563eb',
                false,
                @creator_id,
                now(),
                now())
            RETURNING id;
            """;
        command.Parameters.AddWithValue("workspace_id", workspaceId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("creator_id", creatorId);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<Guid> InsertTaskAsync(
        NpgsqlConnection connection,
        Guid boardId,
        Guid creatorId,
        Guid? assignedToUserId,
        string title)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO tasks (
                id,
                board_id,
                title,
                description,
                status,
                priority,
                created_by_user_id,
                assigned_to_user_id,
                created_at,
                updated_at)
            VALUES (
                gen_random_uuid(),
                @board_id,
                @title,
                '',
                'Todo',
                'Medium',
                @creator_id,
                @assigned_to_user_id,
                now(),
                now())
            RETURNING id;
            """;
        command.Parameters.AddWithValue("board_id", boardId);
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("creator_id", creatorId);
        command.Parameters.AddWithValue("assigned_to_user_id", assignedToUserId ?? (object)DBNull.Value);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<(Guid AdminUserId, Guid WorkspaceId, Guid BoardId)> ReadSeedIdsAsync(
        NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT user_record.id, workspace_record.id, board_record.id
            FROM users AS user_record
            INNER JOIN workspaces AS workspace_record
                ON workspace_record.created_by_user_id = user_record.id
            INNER JOIN boards AS board_record
                ON board_record.workspace_id = workspace_record.id
            WHERE user_record.normalized_username = 'ADMIN'
            LIMIT 1;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2));
    }

    private static async Task ExecuteDeleteAsync(
        NpgsqlConnection connection,
        string table,
        Guid id)
    {
        Assert.Contains(table, new[] { "boards", "users" });
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table} WHERE id = @id;";
        command.Parameters.AddWithValue("id", id);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<bool> RowExistsAsync(
        NpgsqlConnection connection,
        string table,
        Guid id)
    {
        Assert.Contains(table, new[] { "tasks", "users" });
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT EXISTS (SELECT 1 FROM {table} WHERE id = @id);";
        command.Parameters.AddWithValue("id", id);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<Guid?> ReadTaskAssigneeAsync(NpgsqlConnection connection, Guid taskId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT assigned_to_user_id FROM tasks WHERE id = @id;";
        command.Parameters.AddWithValue("id", taskId);
        var result = await command.ExecuteScalarAsync();
        return result is DBNull ? null : (Guid?)result;
    }
}
