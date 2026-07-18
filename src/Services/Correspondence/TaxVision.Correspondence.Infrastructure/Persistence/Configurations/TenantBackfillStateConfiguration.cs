using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Correspondence.Domain.Backfill;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Configurations;

internal sealed class TenantBackfillStateConfiguration : IEntityTypeConfiguration<TenantBackfillState>
{
    public void Configure(EntityTypeBuilder<TenantBackfillState> builder)
    {
        builder.ToTable("TenantBackfillStates");

        builder.HasKey(x => x.TenantId);
        builder.Property(x => x.TenantId).ValueGeneratedNever();

        builder.Property(x => x.CompletedAtUtc).IsRequired();
    }
}
