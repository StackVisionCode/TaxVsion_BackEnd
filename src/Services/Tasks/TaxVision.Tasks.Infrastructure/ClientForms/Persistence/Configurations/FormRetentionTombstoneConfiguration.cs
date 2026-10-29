using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.ClientForms.Domain.Retention;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Configurations;

public sealed class FormRetentionTombstoneConfiguration : IEntityTypeConfiguration<FormRetentionTombstone>
{
    public void Configure(EntityTypeBuilder<FormRetentionTombstone> builder)
    {
        builder.ToTable("FormRetentionTombstones");
        builder.HasKey(tombstone => tombstone.Id);
        builder.Property(tombstone => tombstone.TenantId).IsRequired();
        builder.Property(tombstone => tombstone.Rule).HasMaxLength(16).IsRequired();
        builder.Property(tombstone => tombstone.FinalStatus).HasMaxLength(32).IsRequired();
        builder.HasIndex(tombstone => new { tombstone.TenantId, tombstone.FormRequestId });
    }
}
