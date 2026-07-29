using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notification.Domain.Onboarding;

namespace TaxVision.Notification.Infrastructure.Persistence.Configurations;

public sealed class OnboardingReceiptLookupConfiguration : IEntityTypeConfiguration<OnboardingReceiptLookup>
{
    public void Configure(EntityTypeBuilder<OnboardingReceiptLookup> builder)
    {
        builder.ToTable("OnboardingReceiptLookups");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OnboardingId).IsRequired();
        builder.Property(x => x.ReceiptFileId).IsRequired();
        builder.Property(x => x.ReceiptDownloadUrl).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasIndex(x => x.OnboardingId).IsUnique();
    }
}
