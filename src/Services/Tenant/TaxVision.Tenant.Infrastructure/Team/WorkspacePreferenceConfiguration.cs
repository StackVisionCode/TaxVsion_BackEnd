using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Infrastructure.Team;

public sealed class WorkspacePreferenceConfiguration : IEntityTypeConfiguration<WorkspacePreference>
{
    public void Configure(EntityTypeBuilder<WorkspacePreference> builder)
    {
        builder.ToTable("WorkspacePreferences");
        builder.HasKey(p => p.Id);
        // El id nace en el dominio: sin esto EF podría tratar la fila nueva como existente.
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.PreferenceKey).HasMaxLength(WorkspacePreferenceKeys.MaxLength).IsRequired();
        builder.Property(p => p.SchemaVersion).IsRequired();
        builder.Property(p => p.PayloadJson);
        builder.Property(p => p.UpdatedAtUtc).IsRequired();

        // Dos guardados desde la misma versión: el segundo falla (409) en vez de pisar al primero.
        builder.Property(p => p.Version).IsConcurrencyToken();

        // Una fila por usuario y clave, y una plantilla (UserId NULL) por oficina y clave. HasFilter(null) quita el
        // filtro automático "[UserId] IS NOT NULL" que dejaría la plantilla sin unicidad.
        builder
            .HasIndex(p => new
            {
                p.TenantId,
                p.UserId,
                p.PreferenceKey,
            })
            .IsUnique()
            .HasFilter(null);
    }
}
