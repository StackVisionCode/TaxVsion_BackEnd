using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.CloudStorage.Domain.CustomerAccess;

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Configurations;

public sealed class CustomerDocumentAccessRuleConfiguration : IEntityTypeConfiguration<CustomerDocumentAccessRule>
{
    public void Configure(EntityTypeBuilder<CustomerDocumentAccessRule> builder)
    {
        builder.ToTable("CustomerDocumentAccessRules");
        builder.HasKey(rule => rule.Id);
        builder.Property(rule => rule.TenantId).IsRequired();
        builder.Property(rule => rule.CustomerId).IsRequired();
        builder.Property(rule => rule.ResourceType).HasConversion<int>().IsRequired();
        builder.Property(rule => rule.ResourceId).IsRequired();
        builder.Property(rule => rule.Capability).HasConversion<int>().IsRequired();
        builder.Property(rule => rule.SetByUserId).IsRequired();
        builder.Property(rule => rule.SetAtUtc).IsRequired();
        builder.Property(rule => rule.Reason).HasMaxLength(500);

        // Un deny por (customer, item, capability) — set repetido actualiza la fila.
        builder
            .HasIndex(rule => new
            {
                rule.TenantId,
                rule.CustomerId,
                rule.ResourceType,
                rule.ResourceId,
                rule.Capability,
            })
            .IsUnique()
            .HasDatabaseName("IX_CustomerDocumentAccessRules_Customer_Resource_Capability");

        // Lookup del gate al leer: todas las reglas del cliente para resolver per-item.
        builder
            .HasIndex(rule => new { rule.TenantId, rule.CustomerId })
            .HasDatabaseName("IX_CustomerDocumentAccessRules_Tenant_Customer");
    }
}
