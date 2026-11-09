using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Api.Domain.Boards;

namespace TaskFlow.Api.Infrastructure.Persistence.Configurations;

public sealed class BoardConfiguration : IEntityTypeConfiguration<Board>
{
    public void Configure(EntityTypeBuilder<Board> builder)
    {
        builder.ToTable("boards");

        builder.HasKey(board => board.Id);

        builder.Property(board => board.Id)
            .ValueGeneratedNever();

        builder.Property(board => board.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(board => board.Description)
            .HasMaxLength(2_000)
            .IsRequired();

        builder.Property(board => board.Color)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(board => board.Revision)
            .HasDefaultValue(1L);

        builder.HasIndex(board => new { board.WorkspaceId, board.IsArchived });

        builder.HasOne(board => board.Workspace)
            .WithMany(workspace => workspace.Boards)
            .HasForeignKey(board => board.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(board => board.CreatedByUser)
            .WithMany(user => user.CreatedBoards)
            .HasForeignKey(board => board.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
