using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Configurations;

public sealed class DeliveryAttemptConfiguration : IEntityTypeConfiguration<DeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<DeliveryAttempt> builder)
    {
        builder.ToTable("DeliveryAttempts");
        builder.HasKey(a => a.Id);
        // Se agrega vía la colección del aggregate (mismo motivo que SentMessageEventConfiguration).
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.SentMessageId).IsRequired();
        builder.Property(a => a.Lane).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.ProviderCode).HasMaxLength(50).IsRequired();
        builder.Property(a => a.Succeeded).IsRequired();
        builder.Property(a => a.FailureReason).HasMaxLength(500);
        builder.Property(a => a.AttemptedAtUtc).IsRequired();

        builder.HasIndex(a => a.SentMessageId).HasDatabaseName("IX_DeliveryAttempts_SentMessageId");
    }
}
