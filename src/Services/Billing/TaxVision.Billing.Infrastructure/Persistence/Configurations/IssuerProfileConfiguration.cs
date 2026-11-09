using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Billing.Domain.Invoices;

namespace TaxVision.Billing.Infrastructure.Persistence.Configurations;

/// <summary>Perfil del emisor (proyección del perfil de la oficina). Uno por tenant. La dirección va como JSON.</summary>
public sealed class IssuerProfileConfiguration : IEntityTypeConfiguration<IssuerProfile>
{
    public void Configure(EntityTypeBuilder<IssuerProfile> b)
    {
        b.ToTable("IssuerProfiles");
        b.HasKey(p => p.Id);

        b.Property(p => p.TenantId).IsRequired();
        b.Property(p => p.RowVersion).IsRowVersion();
        b.HasIndex(p => p.TenantId).IsUnique();

        b.Property(p => p.Name).HasMaxLength(IssuerProfile.NameMaxLength).IsRequired();
        b.Property(p => p.TaxId).HasMaxLength(IssuerProfile.TaxIdMaxLength);
        b.Property(p => p.Phone).HasMaxLength(IssuerProfile.PhoneMaxLength);
        b.Property(p => p.Email).HasMaxLength(IssuerProfile.EmailMaxLength);
        b.Property(p => p.Website).HasMaxLength(IssuerProfile.WebsiteMaxLength);
        // Versión del perfil de Tenant ya proyectada (0 = filas de antes de la F1).
        b.Property(p => p.SourceVersion).IsRequired().HasDefaultValue(0L);
        b.Property(p => p.DefaultCurrency).HasMaxLength(3).IsRequired().HasDefaultValue("USD");

        b.Property(p => p.Address)
            .HasConversion(new JsonValueConverter<Domain.ValueObjects.Address>())
            .HasColumnType("nvarchar(max)");
    }
}
