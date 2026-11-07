using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Codes.Domain.Reservations;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Codes;

public sealed class LateCommitReviewConfiguration : IEntityTypeConfiguration<LateCommitReview>
{
    public void Configure(EntityTypeBuilder<LateCommitReview> builder)
    {
        builder.ToTable("LateCommitReviews", GrowthSchemas.Codes);
        builder.HasKey(review => review.Id);
        builder.Property(review => review.Id).ValueGeneratedNever();

        builder.Property(review => review.TenantId).IsRequired();
        builder.Property(review => review.CodeReservationId).IsRequired();
        builder.Property(review => review.PaymentSource).HasMaxLength(100).IsRequired();
        builder.Property(review => review.RelatedPaymentId).IsRequired();
        builder.Property(review => review.SourceEventId).IsRequired();
        builder.Property(review => review.ErrorCode).HasMaxLength(200).IsRequired();
        builder.Property(review => review.CreatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(review => review.ResolvedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(review => review.Resolution).HasMaxLength(500);
        builder.Property(review => review.ResolutionNote).HasMaxLength(LateCommitReview.MaxNoteLength);

        builder
            .HasIndex(review => new { review.CodeReservationId, review.SourceEventId })
            .IsUnique()
            .HasDatabaseName("UX_LateCommitReviews_Reservation_Event");
        builder
            .HasIndex(review => new { review.ResolvedAtUtc, review.CreatedAtUtc })
            .HasDatabaseName("IX_LateCommitReviews_Pending");
    }
}
