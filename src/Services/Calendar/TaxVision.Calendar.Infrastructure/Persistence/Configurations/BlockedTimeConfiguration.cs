using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Calendar.Domain.Availability;

namespace TaxVision.Calendar.Infrastructure.Persistence.Configurations;

public sealed class BlockedTimeConfiguration : IEntityTypeConfiguration<BlockedTime>
{
    public void Configure(EntityTypeBuilder<BlockedTime> builder)
    {
        builder.ToTable("BlockedTimes");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        builder.Property(b => b.TenantId).IsRequired();
        builder.Property(b => b.UserId).IsRequired();
        builder.Property(b => b.StartUtc).IsRequired();
        builder.Property(b => b.EndUtc).IsRequired();
        builder.Property(b => b.Reason).HasMaxLength(BlockedTime.MaxReasonLength);
        builder.Property(b => b.CreatedAtUtc).IsRequired();

        // La consulta de disponibilidad entra por persona y rango.
        builder.HasIndex(b => new
        {
            b.TenantId,
            b.UserId,
            b.StartUtc,
            b.EndUtc,
        });
    }
}
