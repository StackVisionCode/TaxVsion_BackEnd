using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Billing.Domain.Numbering;

namespace TaxVision.Billing.Infrastructure.Persistence.Configurations;

/// <summary>Contador de numeración server-side, uno por (tenant, período). RowVersion garantiza la
/// asignación atómica del próximo número bajo concurrencia.</summary>
public sealed class InvoiceNumberSequenceConfiguration : IEntityTypeConfiguration<InvoiceNumberSequence>
{
    public void Configure(EntityTypeBuilder<InvoiceNumberSequence> b)
    {
        b.ToTable("InvoiceNumberSequences");
        b.HasKey(s => s.Id);
        b.Property(s => s.TenantId).IsRequired();
        b.Property(s => s.PeriodKey).HasMaxLength(16).IsRequired();
        b.Property(s => s.Next).IsRequired();
        b.Property(s => s.RowVersion).IsRowVersion();
        b.HasIndex(s => new { s.TenantId, s.PeriodKey }).IsUnique();
    }
}
