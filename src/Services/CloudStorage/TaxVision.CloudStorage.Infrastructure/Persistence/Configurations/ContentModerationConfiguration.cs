using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.CloudStorage.Domain.Moderation;

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Configurations;

public sealed class ContentModerationPolicyConfiguration : IEntityTypeConfiguration<ContentModerationPolicy>
{
    public void Configure(EntityTypeBuilder<ContentModerationPolicy> builder)
    {
        builder.ToTable("ContentModerationPolicies");
        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.ExcludedFolders).HasMaxLength(1000).IsRequired();
        builder.Property(policy => policy.UpdatedAtUtc).IsRequired();
        builder.Property(policy => policy.RowVersion).IsRowVersion();
        builder.Ignore(policy => policy.ExcludedFolderTypes);
    }
}

public sealed class ContentModerationPolicyAuditConfiguration : IEntityTypeConfiguration<ContentModerationPolicyAudit>
{
    public void Configure(EntityTypeBuilder<ContentModerationPolicyAudit> builder)
    {
        builder.ToTable("ContentModerationPolicyAudits");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.OldSettingsJson).IsRequired();
        builder.Property(entry => entry.NewSettingsJson).IsRequired();
        builder.Property(entry => entry.ActorUserId).IsRequired();
        builder.Property(entry => entry.OccurredAtUtc).IsRequired();

        builder.HasIndex(entry => entry.OccurredAtUtc).HasDatabaseName("IX_ContentModerationPolicyAudits_OccurredAt");
    }
}
