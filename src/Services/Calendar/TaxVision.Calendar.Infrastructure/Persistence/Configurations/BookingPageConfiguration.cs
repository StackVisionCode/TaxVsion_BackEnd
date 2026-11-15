using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Calendar.Domain.PublicBookings;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Infrastructure.Persistence.Configurations;

public sealed class BookingPageConfiguration : IEntityTypeConfiguration<BookingPage>
{
    public void Configure(EntityTypeBuilder<BookingPage> builder)
    {
        builder.Ignore(p => p.DomainEvents);
        builder.Ignore(p => p.IcsAlarmMinutes);
        builder.Ignore(p => p.ActiveHosts);
        builder.Ignore(p => p.ActiveHeardAboutOptions);

        builder.ToTable("BookingPages");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.TenantId).IsRequired();

        builder
            .Property(p => p.Slug)
            .HasConversion(slug => slug.Value, value => BookingPageSlug.Create(value).Value)
            .HasMaxLength(40)
            .IsRequired();

        // Inglés obligatorio y español opcional: dos columnas, así que OwnsOne.
        builder.OwnsOne(
            p => p.Title,
            title =>
            {
                title
                    .Property(t => t.En)
                    .HasColumnName("TitleEn")
                    .HasMaxLength(BookingPage.MaxTitleLength)
                    .IsRequired();
                title.Property(t => t.Es).HasColumnName("TitleEs").HasMaxLength(BookingPage.MaxTitleLength);
            }
        );
        builder.OwnsOne(
            p => p.Description,
            description =>
            {
                description
                    .Property(d => d.En)
                    .HasColumnName("DescriptionEn")
                    .HasMaxLength(BookingPage.MaxDescriptionLength)
                    .IsRequired();
                description
                    .Property(d => d.Es)
                    .HasColumnName("DescriptionEs")
                    .HasMaxLength(BookingPage.MaxDescriptionLength);
            }
        );
        ConfigureConsent(builder);
        builder.Navigation(p => p.Title).IsRequired();
        builder.Navigation(p => p.Description).IsRequired();

        builder.Property(p => p.AppointmentTypeId).IsRequired();
        builder.Property(p => p.IsActive).IsRequired();
        builder.Property(p => p.IcsAlarmMinutesJson).HasMaxLength(100).IsRequired();
        builder.Property(p => p.PhoneField).HasConversion<int>().IsRequired();
        builder.Property(p => p.OfficeSizeField).HasConversion<int>().IsRequired();
        builder.Property(p => p.InterestField).HasConversion<int>().IsRequired();
        builder.Property(p => p.ConsentVersion).HasMaxLength(BookingPage.MaxConsentVersionLength).IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasIndex(p => new { p.TenantId, p.Slug }).IsUnique();

        builder.HasMany(p => p.Hosts).WithOne().HasForeignKey(h => h.BookingPageId).OnDelete(DeleteBehavior.Cascade);
        builder
            .HasMany(p => p.HeardAboutOptions)
            .WithOne()
            .HasForeignKey(o => o.BookingPageId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Hosts).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.HeardAboutOptions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureConsent(EntityTypeBuilder<BookingPage> builder)
    {
        builder.OwnsOne(
            p => p.PrivacyNotice,
            notice =>
            {
                notice
                    .Property(n => n.En)
                    .HasColumnName("PrivacyNoticeEn")
                    .HasMaxLength(BookingPage.MaxConsentTextLength);
                notice
                    .Property(n => n.Es)
                    .HasColumnName("PrivacyNoticeEs")
                    .HasMaxLength(BookingPage.MaxConsentTextLength);
            }
        );
        builder.OwnsOne(
            p => p.MarketingConsent,
            consent =>
            {
                consent
                    .Property(c => c.En)
                    .HasColumnName("MarketingConsentEn")
                    .HasMaxLength(BookingPage.MaxConsentTextLength);
                consent
                    .Property(c => c.Es)
                    .HasColumnName("MarketingConsentEs")
                    .HasMaxLength(BookingPage.MaxConsentTextLength);
            }
        );
    }
}
