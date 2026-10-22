using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Postmaster.Domain.Feedback;
using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Configurations;

public sealed class UnmatchedDeliveryFeedbackConfiguration : IEntityTypeConfiguration<UnmatchedDeliveryFeedback>
{
    public void Configure(EntityTypeBuilder<UnmatchedDeliveryFeedback> builder)
    {
        builder.ToTable("UnmatchedDeliveryFeedback");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.Source).HasMaxLength(40).IsRequired();
        builder.Property(f => f.SourceEventId).HasMaxLength(DeliveryFeedback.MaxSourceEventIdLength).IsRequired();
        builder.Property(f => f.MaskedRecipient).HasMaxLength(320);
        builder.Property(f => f.CorrelationHint).HasMaxLength(500);
        builder.Property(f => f.RawPayload).HasMaxLength(UnmatchedDeliveryFeedback.MaxRawPayloadLength);
        builder.Property(f => f.ReceivedAtUtc).IsRequired();

        // La limpieza de 30 días borra por fecha.
        builder.HasIndex(f => f.ReceivedAtUtc).HasDatabaseName("IX_UnmatchedDeliveryFeedback_ReceivedAtUtc");
    }
}
