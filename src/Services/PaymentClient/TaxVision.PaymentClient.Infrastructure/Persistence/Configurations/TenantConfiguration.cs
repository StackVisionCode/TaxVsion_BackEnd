using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.PaymentClient.Domain.Tenants;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");
        builder.HasKey(tenant => tenant.Id);

        // Id no es generado por la BD: viene de TenantCreatedIntegrationEvent.NewTenantId.
        builder.Property(tenant => tenant.Id).ValueGeneratedNever();

        builder.Property(tenant => tenant.Name).HasMaxLength(200).IsRequired();
        builder.Property(tenant => tenant.SubDomain).HasMaxLength(100).IsRequired();
        builder.Property(tenant => tenant.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(tenant => tenant.DefaultTimeZoneId).HasMaxLength(100).IsRequired();
        builder.Property(tenant => tenant.Status).HasMaxLength(30).IsRequired();
        builder.Property(tenant => tenant.IsActive).IsRequired();

        builder.HasIndex(tenant => tenant.SubDomain).IsUnique().HasDatabaseName("UX_Tenants_SubDomain");
    }
}
