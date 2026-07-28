using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Billing.Domain.Invoices;

namespace TaxVision.Billing.Infrastructure.Persistence.Configurations;

/// <summary>Perfil del emisor (empresa del tenant). Uno por tenant. La dirección va como JSON.</summary>
public sealed class IssuerProfileConfiguration : IEntityTypeConfiguration<IssuerProfile>
{
    public void Configure(EntityTypeBuilder<IssuerProfile> b)
    {
        b.ToTable("IssuerProfiles");
        b.HasKey(p => p.Id);

        b.Property(p => p.TenantId).IsRequired();
        b.HasIndex(p => p.TenantId).IsUnique();

        b.Property(p => p.Name).HasMaxLength(256).IsRequired();
        b.Property(p => p.TaxId).HasMaxLength(64);
        b.Property(p => p.Phone).HasMaxLength(64);
        b.Property(p => p.Email).HasMaxLength(256);
        b.Property(p => p.Website).HasMaxLength(256);

        b.Property(p => p.Address)
            .HasConversion(new JsonValueConverter<Domain.ValueObjects.Address>())
            .HasColumnType("nvarchar(max)");
    }
}
