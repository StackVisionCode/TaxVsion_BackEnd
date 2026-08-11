using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.OptOut;
using TaxVision.Sms.Domain.Webhooks;

namespace TaxVision.Sms.Infrastructure.Persistence.Configurations;

public sealed class SmsMessageConfiguration : IEntityTypeConfiguration<SmsMessage>
{
    public void Configure(EntityTypeBuilder<SmsMessage> builder)
    {
        builder.ToTable("smsMessages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.TenantId).IsRequired();
        builder.Property(m => m.CustomerId).IsRequired();
        builder.Property(m => m.To).HasMaxLength(32).IsRequired();
        builder.Property(m => m.Body).HasColumnType("nvarchar(max)").IsRequired();

        builder.Property(m => m.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(m => m.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(m => m.BatchId).IsRequired();
        builder.Property(m => m.ProviderCode).HasMaxLength(64).IsRequired();
        builder.Property(m => m.SourceContext).HasMaxLength(128);

        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(m => m.ProviderMessageId).HasMaxLength(200);
        builder.Property(m => m.FailureCode).HasMaxLength(64);
        builder.Property(m => m.FailureReason).HasMaxLength(512);

        builder.Property(m => m.CreatedAtUtc).IsRequired();
        builder.Property(m => m.UpdatedAtUtc).IsRequired();

        builder.HasMany(m => m.Media).WithOne().HasForeignKey(x => x.SmsMessageId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(SmsMessage.Media))!.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(m => new { m.TenantId, m.IdempotencyKey }).IsUnique().HasDatabaseName("UX_SmsMessages_Tenant_Idempotency");
        builder.HasIndex(m => new { m.TenantId, m.CustomerId, m.CreatedAtUtc }).HasDatabaseName("IX_SmsMessages_Tenant_Customer_Created");
        builder.HasIndex(m => new { m.ProviderCode, m.ProviderMessageId }).HasDatabaseName("IX_SmsMessages_Provider_MessageId");
        builder.HasIndex(m => m.CorrelationId).HasDatabaseName("IX_SmsMessages_CorrelationId");
        builder.HasIndex(m => m.BatchId).HasDatabaseName("IX_SmsMessages_BatchId");
        builder.HasIndex(m => m.To).HasDatabaseName("IX_SmsMessages_To");
    }
}

public sealed class SmsMediaConfiguration : IEntityTypeConfiguration<SmsMedia>
{
    public void Configure(EntityTypeBuilder<SmsMedia> builder)
    {
        builder.ToTable("smsMedia");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SmsMessageId).IsRequired();
        builder.Property(x => x.Url).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(256);
        builder.Property(x => x.ProviderMediaId).HasMaxLength(200);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.HasIndex(x => x.SmsMessageId).HasDatabaseName("IX_SmsMedia_SmsMessageId");
    }
}

public sealed class SmsOptOutConfiguration : IEntityTypeConfiguration<SmsOptOut>
{
    public void Configure(EntityTypeBuilder<SmsOptOut> builder)
    {
        builder.ToTable("smsOptOuts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.CustomerId).IsRequired();
        builder.Property(x => x.PhoneE164).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.LastKeyword).HasMaxLength(16);
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.CustomerId, x.PhoneE164 }).IsUnique().HasDatabaseName("UX_SmsOptOuts_Tenant_Customer_Phone");
        builder.HasIndex(x => new { x.TenantId, x.PhoneE164 }).HasDatabaseName("IX_SmsOptOuts_Tenant_Phone");
    }
}

public sealed class ProcessedWebhookConfiguration : IEntityTypeConfiguration<ProcessedWebhook>
{
    public void Configure(EntityTypeBuilder<ProcessedWebhook> builder)
    {
        builder.ToTable("processedWebhooks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProviderCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ProviderMessageId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.EventType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PayloadHash).HasMaxLength(128);
        builder.Property(x => x.ProcessedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.ProviderCode, x.ProviderMessageId, x.EventType }).IsUnique().HasDatabaseName("UX_ProcessedWebhooks_Provider_MessageId_EventType");
    }
}
