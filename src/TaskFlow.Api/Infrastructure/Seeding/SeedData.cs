using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Domain.Boards;
using TaskFlow.Api.Domain.Tasks;
using TaskFlow.Api.Domain.Users;
using TaskFlow.Api.Domain.Workspaces;
using TaskFlow.Api.Features.Shared;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Persistence;
using TaskFlow.Api.Infrastructure.Text;

namespace TaskFlow.Api.Infrastructure.Seeding;

public static class SeedData
{
    public static async Task ApplyAsync(
        AppDbContext db,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var username = configuration["Auth:AdminUsername"] ?? "admin";
        var email = configuration["Auth:AdminEmail"] ?? "admin@taskflow.local";
        var displayName = configuration["Auth:AdminDisplayName"] ?? "TaskFlow Admin";
        if (InputValidation.ValidateUsername(username) is { } usernameError)
        {
            throw new InvalidOperationException($"Auth:AdminUsername is invalid. {usernameError}");
        }

        if (InputValidation.ValidateEmail(email) is { } emailError)
        {
            throw new InvalidOperationException($"Auth:AdminEmail is invalid. {emailError}");
        }

        if (InputValidation.ValidateDisplayName(displayName) is { } displayNameError)
        {
            throw new InvalidOperationException($"Auth:AdminDisplayName is invalid. {displayNameError}");
        }

        var normalizedUsername = IdentityNormalizer.Normalize(username);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await SeedDataLock.AcquireAsync(db, cancellationToken);
        await WorkspaceSlugAllocationLock.AcquireAsync(db, cancellationToken);
        var admin = await db.Users.SingleOrDefaultAsync(
            user => user.NormalizedUsername == normalizedUsername,
            cancellationToken);

        if (admin is null)
        {
            var password = configuration["Auth:AdminPassword"];
            if (InputValidation.ValidatePassword(password) is not null)
            {
                throw new InvalidOperationException(
                    "Auth:AdminPassword (env Auth__AdminPassword) must be between 12 and 200 characters " +
                    "on first startup so the admin user can be created. Generate one:  openssl rand -base64 24");
            }

            var now = DateTimeOffset.UtcNow;
            admin = new User
            {
                Username = username.Trim(),
                NormalizedUsername = normalizedUsername,
                Email = email.Trim(),
                NormalizedEmail = IdentityNormalizer.Normalize(email),
                DisplayName = displayName.Trim(),
                PasswordHash = PasswordHasher.Hash(password!),
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Users.Add(admin);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seed data: created admin user '{Username}'.", admin.Username);
        }
        else
        {
            logger.LogInformation("Seed data: admin user already exists, skipping account creation.");
        }

        var workspace = await db.Workspaces
            .FirstOrDefaultAsync(
                candidate => candidate.CreatedByUserId == admin.Id &&
                    (candidate.Slug == "taskflow" || candidate.Name == "TaskFlow"),
                cancellationToken);
        if (workspace is null)
        {
            var now = DateTimeOffset.UtcNow;
            workspace = new Workspace
            {
                Name = "TaskFlow",
                Slug = await SlugGenerator.CreateUniqueAsync(db, "taskflow", cancellationToken),
                CreatedByUserId = admin.Id,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Workspaces.Add(workspace);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!await db.WorkspaceMemberships.AnyAsync(
                membership => membership.WorkspaceId == workspace.Id && membership.UserId == admin.Id,
                cancellationToken))
        {
            db.WorkspaceMemberships.Add(new WorkspaceMembership
            {
                WorkspaceId = workspace.Id,
                UserId = admin.Id,
                Role = WorkspaceRole.Owner,
                JoinedAt = DateTimeOffset.UtcNow,
            });
        }

        if (await db.Boards.AnyAsync(board => board.WorkspaceId == workspace.Id, cancellationToken))
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Seed data: workspace data already exists, skipping board creation.");
            return;
        }

        var current = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var board = new Board
        {
            WorkspaceId = workspace.Id,
            Name = "Deploy to production",
            Description = "The production deployment path used throughout the course.",
            Color = InputValidation.DefaultBoardColor,
            CreatedByUserId = admin.Id,
            CreatedAt = current.AddDays(-9),
            UpdatedAt = current,
            Tasks =
            [
                CreateTask("Provision a VPS", TaskState.Done, TaskPriority.High, 0, current.AddDays(-9), current.AddDays(-7), admin.Id),
                CreateTask("Harden SSH access", TaskState.Done, TaskPriority.Urgent, 1, current.AddDays(-8), current.AddDays(-6), admin.Id),
                CreateTask("Configure the firewall", TaskState.Done, TaskPriority.High, 2, current.AddDays(-8), current.AddDays(-5), admin.Id),
                CreateTask("Set up private networking", TaskState.Done, TaskPriority.Medium, 3, current.AddDays(-6), current.AddDays(-2), admin.Id),
                CreateTask("Install Dokploy", TaskState.Done, TaskPriority.High, 4, current.AddDays(-5), current.AddMinutes(-20), admin.Id),
                CreateTask("Point DNS at the server", TaskState.Doing, TaskPriority.High, 5, current.AddDays(-4), null, admin.Id, today.AddDays(1)),
                CreateTask("Deploy this app", TaskState.Doing, TaskPriority.Urgent, 6, current.AddDays(-3), null, admin.Id, today),
                CreateTask("Set up automatic deployments", TaskState.Todo, TaskPriority.Medium, 7, current.AddDays(-2), null, admin.Id, today.AddDays(2)),
                CreateTask("Schedule database backups", TaskState.Todo, TaskPriority.High, 8, current.AddDays(-1), null, admin.Id, today.AddDays(3)),
                CreateTask("Run a restore drill", TaskState.Todo, TaskPriority.Medium, 9, current.AddHours(-4), null, admin.Id, today.AddDays(7)),
            ],
        };
        db.Boards.Add(board);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Seed data: created workspace board with {Count} tasks.", board.Tasks.Count);
    }

    private static TaskItem CreateTask(
        string title,
        TaskState status,
        TaskPriority priority,
        int position,
        DateTimeOffset createdAt,
        DateTimeOffset? completedAt,
        Guid createdByUserId,
        DateOnly? dueDate = null) =>
        new()
        {
            Title = title,
            Status = status,
            Priority = priority,
            Position = position,
            CreatedByUserId = createdByUserId,
            AssignedToUserId = createdByUserId,
            CreatedAt = createdAt,
            UpdatedAt = completedAt ?? createdAt,
            CompletedAt = completedAt,
            DueDate = dueDate,
        };
}
