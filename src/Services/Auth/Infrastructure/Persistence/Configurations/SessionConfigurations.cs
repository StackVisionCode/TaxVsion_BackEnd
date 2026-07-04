using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Infrastructure.Persistence.Configurations;

/// <summary>Mapeo EF Core de UserSession: tabla, propiedad calculada IsActive ignorada, índices y relación en cascada con el usuario.</summary>
public sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("UserSessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.TenantId).IsRequired();
        builder.Property(session => session.UserId).IsRequired();
        builder.Property(session => session.DeviceName).HasMaxLength(100);
        builder.Property(session => session.UserAgent).HasMaxLength(512);
        builder.Property(session => session.IpAddress).HasMaxLength(45);
        builder.Property(session => session.RevokedReason).HasMaxLength(64);
        builder.Property(session => session.CreatedAtUtc).IsRequired();
        builder.Property(session => session.LastSeenAtUtc).IsRequired();
        builder.Ignore(session => session.IsActive);

        builder.HasIndex(session => new { session.UserId, session.RevokedAtUtc });
        builder.HasIndex(session => session.TenantId);

        builder.HasOne<User>().WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
