using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DomainTenant = TaxVision.Tenant.Domain.Tenant;


namespace TaxVision.Tenant.Infrastructure.Persistence.Configurations;

// Configura cómo se mapea la entidad Tenant a la tabla SQL.
public sealed class TenantConfiguration : IEntityTypeConfiguration<DomainTenant>
{
    public void Configure(EntityTypeBuilder<DomainTenant> b)
    {
        b.ToTable("Tenants");
        b.HasKey(t => t.Id);
        b.Property(t => t.Name).HasMaxLength(200).IsRequired();
        b.Property(t => t.SubDomain).HasMaxLength(40).IsRequired();
        b.Property(t => t.Status).HasConversion<string>(); // enum como texto legible
                                                           // El subdominio es único globalmente (a diferencia del email por tenant).
        b.HasIndex(t => t.SubDomain).IsUnique();
    }
}
