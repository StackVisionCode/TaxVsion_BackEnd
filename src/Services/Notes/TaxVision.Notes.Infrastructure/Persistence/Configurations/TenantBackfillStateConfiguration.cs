using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notes.Domain.Backfill;

namespace TaxVision.Notes.Infrastructure.Persistence.Configurations;

public sealed class TenantBackfillStateConfiguration : IEntityTypeConfiguration<TenantBackfillState>
{
    public void Configure(EntityTypeBuilder<TenantBackfillState> builder)
    {
        builder.ToTable("TenantBackfillStates");
        builder.HasKey(x => x.TenantId);
        builder.Property(x => x.TenantId).ValueGeneratedNever();
        builder.Property(x => x.CompletedAtUtc).IsRequired();
    }
}
