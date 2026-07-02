using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Auth.Domain.Invitations;

namespace TaxVision.Auth.Infrastructure.Persistence.Configurations;

public sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("Invitations");
        builder.HasKey(invitation => invitation.Id);

        builder.Property(invitation => invitation.TenantId).IsRequired();
        builder.Property(invitation => invitation.Email)
            .HasMaxLength(320)
            .IsRequired();
        builder.Property(invitation => invitation.ActorType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(invitation => invitation.TokenHash)
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(invitation => invitation.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(invitation => invitation.CreatedAtUtc).IsRequired();
        builder.Property(invitation => invitation.ExpiresAtUtc).IsRequired();
        builder.Property(invitation => invitation.RoleIdsJson).HasMaxLength(1024);
        builder.Property(invitation => invitation.ResendCount).IsRequired();

        builder.HasIndex(invitation => invitation.TokenHash).IsUnique();
        builder.HasIndex(invitation => new
            {
                invitation.TenantId,
                invitation.Email,
                invitation.Status
            });
        builder.HasIndex(invitation => invitation.ExpiresAtUtc);
    }
}
