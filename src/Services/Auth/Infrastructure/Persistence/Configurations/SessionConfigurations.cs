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
        builder.Property(session => session.StartIpAddress).HasMaxLength(45);
        builder.Property(session => session.RevokedReason).HasMaxLength(64);
        builder.Property(session => session.CreatedAtUtc).IsRequired();
        builder.Property(session => session.LastSeenAtUtc).IsRequired();
        // Las sesiones anteriores al canal eran todas web.
        builder
            .Property(session => session.Channel)
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasDefaultValue(SessionChannel.Web)
            .IsRequired();
        builder.Ignore(session => session.IsActive);

        // Ubicación aproximada al iniciar; todas las columnas opcionales.
        builder.OwnsOne(
            session => session.Location,
            location =>
            {
                location.Property(value => value.CountryCode).HasColumnName("LocationCountryCode").HasMaxLength(2);
                location.Property(value => value.Region).HasColumnName("LocationRegion").HasMaxLength(128);
                location.Property(value => value.City).HasColumnName("LocationCity").HasMaxLength(128);
                location.Property(value => value.Latitude).HasColumnName("LocationLatitude");
                location.Property(value => value.Longitude).HasColumnName("LocationLongitude");
                location.Property(value => value.Source).HasColumnName("LocationSource").HasMaxLength(16);
                location.Property(value => value.ResolvedAtUtc).HasColumnName("LocationResolvedAtUtc");
            }
        );

        builder.HasIndex(session => new { session.UserId, session.RevokedAtUtc });
        builder.HasIndex(session => session.TenantId);

        builder.HasOne<User>().WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
