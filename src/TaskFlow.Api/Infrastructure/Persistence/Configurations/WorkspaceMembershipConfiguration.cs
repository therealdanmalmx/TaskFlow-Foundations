using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Api.Domain.Workspaces;

namespace TaskFlow.Api.Infrastructure.Persistence.Configurations;

public sealed class WorkspaceMembershipConfiguration : IEntityTypeConfiguration<WorkspaceMembership>
{
    public void Configure(EntityTypeBuilder<WorkspaceMembership> builder)
    {
        builder.ToTable("workspace_memberships");

        builder.HasKey(membership => new { membership.WorkspaceId, membership.UserId });

        builder.Property(membership => membership.Role)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.HasIndex(membership => membership.UserId);

        builder.HasIndex(membership => new { membership.WorkspaceId, membership.Role });

        builder.HasOne(membership => membership.Workspace)
            .WithMany(workspace => workspace.Memberships)
            .HasForeignKey(membership => membership.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(membership => membership.User)
            .WithMany(user => user.WorkspaceMemberships)
            .HasForeignKey(membership => membership.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
