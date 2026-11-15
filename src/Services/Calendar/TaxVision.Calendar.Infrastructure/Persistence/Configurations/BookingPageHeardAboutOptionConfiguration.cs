using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Infrastructure.Persistence.Configurations;

public sealed class BookingPageHeardAboutOptionConfiguration : IEntityTypeConfiguration<BookingPageHeardAboutOption>
{
    public void Configure(EntityTypeBuilder<BookingPageHeardAboutOption> builder)
    {
        builder.ToTable("BookingPageHeardAboutOptions");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.BookingPageId).IsRequired();
        builder.Property(o => o.Code).HasMaxLength(40).IsRequired();
        builder.Property(o => o.LabelEn).HasMaxLength(BookingPageHeardAboutOption.MaxLabelLength).IsRequired();
        builder.Property(o => o.LabelEs).HasMaxLength(BookingPageHeardAboutOption.MaxLabelLength);

        builder.HasIndex(o => new { o.BookingPageId, o.Code }).IsUnique();
    }
}
