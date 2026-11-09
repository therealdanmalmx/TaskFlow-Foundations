using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Api.Domain.Workspaces;

namespace TaskFlow.Api.Infrastructure.Persistence.Configurations;

public sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.ToTable("workspaces");

        builder.HasKey(workspace => workspace.Id);

        builder.Property(workspace => workspace.Id)
            .ValueGeneratedNever();

        builder.Property(workspace => workspace.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(workspace => workspace.Slug)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(workspace => workspace.Slug)
            .IsUnique();

        builder.HasOne(workspace => workspace.CreatedByUser)
            .WithMany(user => user.CreatedWorkspaces)
            .HasForeignKey(workspace => workspace.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
