using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Campaigns.Domain.Senders;

namespace TaxVision.Campaigns.Infrastructure.Persistence.Configurations;

public sealed class SenderProfileConfiguration : IEntityTypeConfiguration<SenderProfile>
{
    public void Configure(EntityTypeBuilder<SenderProfile> builder)
    {
        builder.ToTable("SenderProfiles");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.Channel).HasConversion<int>().IsRequired();
        builder.Property(s => s.Name).HasMaxLength(SenderProfile.MaxNameLength).IsRequired();
        builder.Property(s => s.SenderRef).HasMaxLength(SenderProfile.MaxSenderRefLength).IsRequired();
        builder.Property(s => s.Status).HasConversion<int>().IsRequired();
        builder.Property(s => s.CreatedAtUtc).IsRequired();
        builder.Property(s => s.UpdatedAtUtc).IsRequired();

        builder.HasIndex(s => new { s.TenantId, s.Channel }).HasDatabaseName("IX_SenderProfiles_TenantId_Channel");
        builder.HasIndex(s => new { s.TenantId, s.Status }).HasDatabaseName("IX_SenderProfiles_TenantId_Status");
    }
}
