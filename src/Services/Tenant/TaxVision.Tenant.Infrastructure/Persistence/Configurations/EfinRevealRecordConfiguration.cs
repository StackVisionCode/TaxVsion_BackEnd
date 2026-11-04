using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tenant.Domain.Compliance;

namespace TaxVision.Tenant.Infrastructure.Persistence.Configurations;

/// <summary>Constancias de revelado del EFIN: solo inserciones, consultadas por oficina y fecha.</summary>
public sealed class EfinRevealRecordConfiguration : IEntityTypeConfiguration<EfinRevealRecord>
{
    public void Configure(EntityTypeBuilder<EfinRevealRecord> b)
    {
        b.ToTable("EfinRevealRecords");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.TenantId).IsRequired();
        b.Property(r => r.RevealedByUserId).IsRequired();
        b.Property(r => r.RevealedAtUtc).IsRequired();
        b.HasIndex(r => new { r.TenantId, r.RevealedAtUtc });
    }
}
