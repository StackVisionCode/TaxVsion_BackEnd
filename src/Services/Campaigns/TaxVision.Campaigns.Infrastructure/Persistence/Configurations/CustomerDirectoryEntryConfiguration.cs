using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Campaigns.Infrastructure.Customers.Directory;

namespace TaxVision.Campaigns.Infrastructure.Persistence.Configurations;

public sealed class CustomerDirectoryEntryConfiguration : IEntityTypeConfiguration<CustomerDirectoryEntry>
{
    public void Configure(EntityTypeBuilder<CustomerDirectoryEntry> builder)
    {
        builder.ToTable("CustomerDirectoryEntries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.CustomerId).IsRequired();
        builder.Property(e => e.DisplayName).HasMaxLength(300).IsRequired();
        builder.Property(e => e.Email).HasMaxLength(320).IsRequired();
        builder.Property(e => e.PhoneE164).HasMaxLength(20);
        builder.Property(e => e.Status).HasMaxLength(16).IsRequired();
        builder.Property(e => e.Version).IsRequired();

        // Una entrada por (tenant, cliente); lookup contacto⇔cliente por email.
        builder.HasIndex(e => new { e.TenantId, e.CustomerId }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.Email });
    }
}
