using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.Domain.Backfill;

namespace TaxVision.Tasks.Infrastructure.Persistence.Configurations;

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
