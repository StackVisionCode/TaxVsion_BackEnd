using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Infrastructure.Persistence.Configurations;

public sealed class PushDeviceTokenConfiguration : IEntityTypeConfiguration<PushDeviceToken>
{
    public void Configure(EntityTypeBuilder<PushDeviceToken> builder)
    {
        builder.ToTable("PushDeviceTokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TenantId).IsRequired();
        builder.Property(t => t.UserId).IsRequired();
        builder.Property(t => t.Platform).HasConversion<string>().HasMaxLength(10).IsRequired();
        // 400 chars cubre FCM (~163) y APNs (64 hex, ~200 en VoIP push) con margen,
        // y entra dentro del limite de 900 bytes para columnas indexadas de SQL
        // Server (400 * 2 bytes nvarchar = 800 bytes) — el indice UNIQUE de abajo
        // lo necesita.
        builder.Property(t => t.Token).HasMaxLength(400).IsRequired();
        builder.Property(t => t.DeviceId).HasMaxLength(128);
        builder.Property(t => t.IsActive).IsRequired();
        builder.Property(t => t.RegisteredAtUtc).IsRequired();

        // Un token es unico por tenant — reinstalaciones/reasignaciones reactivan
        // la fila existente (ver PushDeviceToken.Reactivate) en vez de duplicar.
        builder.HasIndex(t => new { t.TenantId, t.Token }).IsUnique();
        builder.HasIndex(t => new
        {
            t.TenantId,
            t.UserId,
            t.IsActive,
        });
    }
}
