using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Customer.Domain.Employees;

namespace TaxVision.Customer.Infrastructure.Persistence.Configurations;

internal sealed class TenantEmployeeDirectoryEntryConfiguration : IEntityTypeConfiguration<TenantEmployeeDirectoryEntry>
{
    public void Configure(EntityTypeBuilder<TenantEmployeeDirectoryEntry> builder)
    {
        builder.ToTable("TenantEmployeeDirectoryEntries");

        builder.HasKey(e => e.UserId);
        builder.Property(e => e.UserId).ValueGeneratedNever();

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.ActorType).IsRequired().HasMaxLength(50);
        builder.Property(e => e.IsActive).IsRequired();
        builder.Property(e => e.UpdatedAtUtc).IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.IsActive });
    }
}
