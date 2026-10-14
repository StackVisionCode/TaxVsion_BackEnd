using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.CloudStorage.Domain.CustomerAccess;

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Configurations;

public sealed class CustomerAccessRuleAuditConfiguration : IEntityTypeConfiguration<CustomerAccessRuleAudit>
{
    public void Configure(EntityTypeBuilder<CustomerAccessRuleAudit> builder)
    {
        builder.ToTable("CustomerAccessRuleAudits");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.TenantId).IsRequired();
        builder.Property(entry => entry.CustomerId).IsRequired();
        builder.Property(entry => entry.ResourceType).HasConversion<int>().IsRequired();
        builder.Property(entry => entry.ResourceId).IsRequired();
        builder.Property(entry => entry.Capability).HasConversion<int>().IsRequired();
        builder.Property(entry => entry.Action).HasConversion<int>().IsRequired();
        builder.Property(entry => entry.ActorUserId).IsRequired();
        builder.Property(entry => entry.OccurredAtUtc).IsRequired();
        builder.Property(entry => entry.Reason).HasMaxLength(500);

        builder
            .HasIndex(entry => new
            {
                entry.TenantId,
                entry.CustomerId,
                entry.OccurredAtUtc,
            })
            .HasDatabaseName("IX_CustomerAccessRuleAudits_Tenant_Customer_OccurredAt");
        builder
            .HasIndex(entry => new { entry.TenantId, entry.OccurredAtUtc })
            .HasDatabaseName("IX_CustomerAccessRuleAudits_Tenant_OccurredAt");
    }
}
