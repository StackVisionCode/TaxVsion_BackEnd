using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Billing.Infrastructure.Persistence.Archive;

namespace TaxVision.Billing.Infrastructure.Persistence.Configurations;

/// <summary>Archivo de las facturas de alta (ONB-) que ya no viven en Invoices. Lo llena la migración.</summary>
public sealed class ArchivedPlatformInvoiceConfiguration : IEntityTypeConfiguration<ArchivedPlatformInvoice>
{
    public void Configure(EntityTypeBuilder<ArchivedPlatformInvoice> b)
    {
        b.ToTable("ArchivedPlatformInvoices");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();
        b.Property(a => a.InvoiceNumber).HasMaxLength(64);
        b.Property(a => a.Total).HasMaxLength(32).IsRequired();
        b.Property(a => a.SnapshotJson).IsRequired();
        b.HasIndex(a => a.OnboardingId).IsUnique();
    }
}
