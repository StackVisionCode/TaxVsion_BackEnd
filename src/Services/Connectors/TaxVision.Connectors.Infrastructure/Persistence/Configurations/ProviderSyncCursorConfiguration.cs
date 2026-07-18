using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Connectors.Domain.Sync;

namespace TaxVision.Connectors.Infrastructure.Persistence.Configurations;

public sealed class ProviderSyncCursorConfiguration : IEntityTypeConfiguration<ProviderSyncCursor>
{
    public void Configure(EntityTypeBuilder<ProviderSyncCursor> builder)
    {
        builder.ToTable("ProviderSyncCursors");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.AccountId).IsRequired();
        builder.Property(c => c.CursorValue).HasMaxLength(2000);
        builder.Property(c => c.UpdatedAtUtc).IsRequired();
        // Defensa en profundidad detrás del IDistributedLock por-cuenta (Fase 4 de hardening) — ver
        // el comentario en ProviderSyncCursor.RowVersion para el razonamiento completo.
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasIndex(c => c.AccountId).IsUnique();
    }
}
