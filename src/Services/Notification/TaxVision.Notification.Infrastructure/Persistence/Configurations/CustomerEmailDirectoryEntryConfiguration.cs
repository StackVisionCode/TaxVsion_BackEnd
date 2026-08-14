using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Infrastructure.Persistence.Configurations;

public sealed class CustomerEmailDirectoryEntryConfiguration : IEntityTypeConfiguration<CustomerEmailDirectoryEntry>
{
    public void Configure(EntityTypeBuilder<CustomerEmailDirectoryEntry> builder)
    {
        builder.ToTable("CustomerEmailDirectoryEntries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.CustomerId).IsRequired();
        builder.Property(e => e.NormalizedEmail).HasMaxLength(320).IsRequired();
        builder.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.IsActive).IsRequired();
        builder.Property(e => e.UpdatedAtUtc).IsRequired();

        // Un cliente, una fila. Los seis consumers pueden llegar en cualquier orden y reintentarse:
        // el índice único es lo que convierte una carrera en un choque visible y no en dos filas.
        builder.HasIndex(e => new { e.TenantId, e.CustomerId }).IsUnique();
    }
}
