using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Infrastructure.Team;

public sealed class ActivityEntryConfiguration : IEntityTypeConfiguration<ActivityEntry>
{
    public void Configure(EntityTypeBuilder<ActivityEntry> builder)
    {
        builder.ToTable("ActivityEntries");
        builder.HasKey(e => e.Id);
        // El id nace en el dominio: sin esto EF podría tratar la fila nueva como existente.
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.EventId).IsRequired();
        builder.Property(e => e.ActorUserId).IsRequired();
        builder.Property(e => e.Verb).HasMaxLength(64).IsRequired();
        builder.Property(e => e.ResourceType).HasMaxLength(32).IsRequired();
        builder.Property(e => e.ResourceId).IsRequired();
        builder.Property(e => e.Visibility).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(e => e.OccurredAtUtc).IsRequired();

        // Idempotencia: una reentrega del mismo evento choca aquí.
        builder.HasIndex(e => new { e.TenantId, e.EventId }).IsUnique();
        // Lectura: actividad de un miembro por fecha.
        builder.HasIndex(e => new
        {
            e.TenantId,
            e.ActorUserId,
            e.OccurredAtUtc,
        });
        // Purga diaria por fecha, de todas las oficinas.
        builder.HasIndex(e => e.OccurredAtUtc);
    }
}
