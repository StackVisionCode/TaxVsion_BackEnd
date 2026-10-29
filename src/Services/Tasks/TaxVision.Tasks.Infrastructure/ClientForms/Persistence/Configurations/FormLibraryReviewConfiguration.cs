using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Configurations;

public sealed class FormLibraryReviewConfiguration : IEntityTypeConfiguration<FormLibraryReview>
{
    public void Configure(EntityTypeBuilder<FormLibraryReview> builder)
    {
        builder.ToTable("FormLibraryReviews");
        builder.HasKey(review => review.Id);
        builder.Property(review => review.Id).ValueGeneratedNever();
        builder.Property(review => review.TenantId).IsRequired();
        builder.Property(review => review.FormTemplateId).IsRequired();
        builder.Property(review => review.ReviewedVersionNumber).IsRequired();
        builder.Property(review => review.ReviewerRole).HasMaxLength(32).IsRequired();
        builder.Property(review => review.ReviewedByUserId).IsRequired();
        builder.Property(review => review.ReviewedAtUtc).IsRequired();
        builder.Property(review => review.Note).HasMaxLength(1000);
        builder
            .HasIndex(review => new { review.FormTemplateId, review.ReviewedVersionNumber })
            .HasDatabaseName("IX_FormLibraryReviews_Template_Version");
    }
}
