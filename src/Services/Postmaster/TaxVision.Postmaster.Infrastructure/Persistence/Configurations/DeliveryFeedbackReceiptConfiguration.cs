using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Postmaster.Domain.Feedback;
using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Configurations;

public sealed class DeliveryFeedbackReceiptConfiguration : IEntityTypeConfiguration<DeliveryFeedbackReceipt>
{
    public void Configure(EntityTypeBuilder<DeliveryFeedbackReceipt> builder)
    {
        builder.ToTable("DeliveryFeedbackReceipts");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Source).HasMaxLength(40).IsRequired();
        builder.Property(r => r.SourceEventId).HasMaxLength(DeliveryFeedback.MaxSourceEventIdLength).IsRequired();
        builder.Property(r => r.SentMessageId).IsRequired();
        builder.Property(r => r.ReceivedAtUtc).IsRequired();

        // Lo que de verdad impide aplicar dos veces el mismo aviso.
        builder
            .HasIndex(r => new { r.Source, r.SourceEventId })
            .IsUnique()
            .HasDatabaseName("UX_DeliveryFeedbackReceipts_Source_SourceEventId");
        builder.HasIndex(r => r.SentMessageId).HasDatabaseName("IX_DeliveryFeedbackReceipts_SentMessageId");
    }
}
