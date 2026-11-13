using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Calendar.Domain.PublicBookings;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Infrastructure.Persistence.Configurations;

public sealed class BookingPageHostConfiguration : IEntityTypeConfiguration<BookingPageHost>
{
    public void Configure(EntityTypeBuilder<BookingPageHost> builder)
    {
        builder.ToTable("BookingPageHosts");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();
        builder.Property(h => h.BookingPageId).IsRequired();
        builder.Property(h => h.UserId).IsRequired();
        builder.Property(h => h.DisplayName).HasMaxLength(BookingPageHost.MaxDisplayNameLength).IsRequired();
        builder.Property(h => h.NotificationEmail).HasMaxLength(WorkEmail.MaxLength).IsRequired();
        builder.Property(h => h.MeetingUrl).HasMaxLength(Location.MaxLength).IsRequired();

        builder.HasIndex(h => new { h.BookingPageId, h.UserId }).IsUnique();
        builder.HasIndex(h => h.UserId);
    }
}
