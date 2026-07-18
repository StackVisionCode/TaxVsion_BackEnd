using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Connectors.Domain.Audit;

namespace TaxVision.Connectors.Infrastructure.Persistence.Configurations;

public sealed class ProviderConnectionAuditLogConfiguration : IEntityTypeConfiguration<ProviderConnectionAuditLog>
{
    public void Configure(EntityTypeBuilder<ProviderConnectionAuditLog> builder)
    {
        builder.ToTable("ProviderConnectionAuditLogs");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.AccountId).IsRequired();
        builder.Property(e => e.Action).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.Detail).HasMaxLength(2000).IsRequired();
        builder.Property(e => e.ResultCode).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Timestamp).IsRequired();

        builder.HasIndex(e => new { e.AccountId, e.Timestamp });
        // Índice propio (Fase 11 — retention): el purge filtra solo por Timestamp, sin AccountId.
        builder.HasIndex(e => e.Timestamp);
    }
}
