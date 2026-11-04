using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tenant.Domain.Compliance;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Infrastructure.Persistence.Configurations;

/// <summary>Cumplimiento de la oficina: una fila por tenant; el EFIN en columnas cifradas (AES-GCM + versión de clave).</summary>
public sealed class OfficeComplianceConfiguration : IEntityTypeConfiguration<OfficeCompliance>
{
    public void Configure(EntityTypeBuilder<OfficeCompliance> b)
    {
        b.ToTable("OfficeCompliances");
        b.HasKey(c => c.TenantId);
        b.Property(c => c.TenantId).ValueGeneratedNever();

        b.Property(c => c.IncidentContactEmail).HasMaxLength(ContactEmail.MaxLength);
        b.Property(c => c.IncidentContactPhone).HasMaxLength(20);

        // Recordatorio del WISP. El día del aviso es token de concurrencia: dos réplicas que anotan el mismo
        // aviso chocan y solo la primera publica.
        b.Property(c => c.WispReminderStage).HasConversion<string>().HasMaxLength(16);
        b.Property(c => c.WispReminderSentOn).IsConcurrencyToken();

        b.OwnsOne(
            c => c.Efin,
            efin =>
            {
                efin.Property(e => e.Ciphertext).HasColumnName("EfinCiphertext").HasMaxLength(64).IsRequired();
                efin.Property(e => e.Nonce).HasColumnName("EfinNonce").HasMaxLength(12).IsRequired();
                efin.Property(e => e.Tag).HasColumnName("EfinTag").HasMaxLength(16).IsRequired();
                efin.Property(e => e.KeyVersion).HasColumnName("EfinKeyVersion").IsRequired();
                efin.Property(e => e.LastDigits)
                    .HasColumnName("EfinLastDigits")
                    .HasMaxLength(Efin.VisibleDigits)
                    .IsRequired();
                efin.Ignore(e => e.Masked);
            }
        );
    }
}
