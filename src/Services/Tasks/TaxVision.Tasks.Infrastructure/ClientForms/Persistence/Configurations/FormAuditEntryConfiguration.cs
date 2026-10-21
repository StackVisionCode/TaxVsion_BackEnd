using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.ClientForms.Domain.Audit;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Configurations;

public sealed class FormAuditEntryConfiguration : IEntityTypeConfiguration<FormAuditEntry>
{
    public void Configure(EntityTypeBuilder<FormAuditEntry> builder)
    {
        builder.ToTable("FormAuditEntries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Action).HasMaxLength(64).IsRequired();
        builder.Property(entry => entry.Outcome).HasMaxLength(32).IsRequired();
        builder.Property(entry => entry.ActorType).HasMaxLength(32).IsRequired();
        builder.Property(entry => entry.IpAddress).HasMaxLength(64);
        builder.Property(entry => entry.UserAgent).HasMaxLength(512);
        builder.Property(entry => entry.CorrelationId).HasMaxLength(64).IsRequired();
        builder.Property(entry => entry.DetailsJson).HasMaxLength(2000);
        builder.HasIndex(entry => new { entry.TenantId, entry.FormRequestId, entry.OccurredAtUtc }).HasDatabaseName("IX_FormAuditEntries_TenantId_Request_OccurredAt");
        builder.HasIndex(entry => new { entry.TenantId, entry.FormTemplateId, entry.OccurredAtUtc }).HasDatabaseName("IX_FormAuditEntries_TenantId_Template_OccurredAt");
    }
}
