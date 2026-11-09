using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Api.Domain.Tasks;

namespace TaskFlow.Api.Infrastructure.Persistence.Configurations;

public sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("tasks");

        builder.HasKey(task => task.Id);

        builder.Property(task => task.Id)
            .ValueGeneratedNever();

        builder.Property(task => task.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(task => task.Description)
            .HasMaxLength(4_000)
            .IsRequired();

        builder.Property(task => task.Status)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(task => task.Priority)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(task => task.Position)
            .HasDefaultValue(0);

        builder.HasIndex(task => new { task.BoardId, task.Status, task.Position });

        builder.HasIndex(task => new { task.BoardId, task.DueDate });

        builder.HasIndex(task => task.AssignedToUserId);

        builder.HasOne(task => task.Board)
            .WithMany(board => board.Tasks)
            .HasForeignKey(task => task.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(task => task.CreatedByUser)
            .WithMany(user => user.CreatedTasks)
            .HasForeignKey(task => task.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(task => task.AssignedToUser)
            .WithMany(user => user.AssignedTasks)
            .HasForeignKey(task => task.AssignedToUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
