using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Postmaster.Domain.Projections;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Configurations;

internal sealed class TenantDirectoryEntryConfiguration : IEntityTypeConfiguration<TenantDirectoryEntry>
{
    public void Configure(EntityTypeBuilder<TenantDirectoryEntry> builder)
    {
        builder.ToTable("TenantDirectory");

        // El TenantId ES la clave: una oficina, una fila. Sin Id propio no hay forma de duplicar.
        builder.HasKey(e => e.TenantId);
        builder.Property(e => e.TenantId).ValueGeneratedNever();

        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.SubDomain).IsRequired().HasMaxLength(100);
        builder.Property(e => e.UpdatedAtUtc).IsRequired();
    }
}
