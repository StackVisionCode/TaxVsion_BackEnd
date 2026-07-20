using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Codes;

internal static class CodesValueObjectConfiguration
{
    public static void ConfigureMoney<TOwner>(
        this OwnedNavigationBuilder<TOwner, Money> builder,
        string columnPrefix
    )
        where TOwner : class
    {
        builder.Property(value => value.AmountCents).HasColumnName($"{columnPrefix}AmountCents");
        builder
            .Property(value => value.Currency)
            .HasColumnName($"{columnPrefix}Currency")
            .HasColumnType("char(3)")
            .IsRequired();
    }

    public static void ConfigureDisplay<TOwner>(
        this OwnedNavigationBuilder<TOwner, CodeDisplay> builder,
        string columnPrefix
    )
        where TOwner : class
    {
        builder
            .Property(value => value.Prefix)
            .HasColumnName($"{columnPrefix}Prefix")
            .HasMaxLength(12)
            .IsRequired();
        builder
            .Property(value => value.LastFour)
            .HasColumnName($"{columnPrefix}LastFour")
            .HasColumnType("char(4)")
            .IsRequired();
    }

    public static void ConfigureSubject<TOwner>(
        this OwnedNavigationBuilder<TOwner, SubjectReference> builder
    )
        where TOwner : class
    {
        builder
            .Property(value => value.Type)
            .HasColumnName("SubjectType")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder
            .Property(value => value.SubjectId)
            .HasColumnName("SubjectId")
            .HasMaxLength(200)
            .IsRequired();
    }

    public static void ConfigureOffer<TOwner>(
        this OwnedNavigationBuilder<TOwner, OfferReference> builder
    )
        where TOwner : class
    {
        builder.Property(value => value.Owner).HasColumnName("OfferOwner").HasMaxLength(100).IsRequired();
        builder.Property(value => value.OfferId).HasColumnName("OfferId").HasMaxLength(200).IsRequired();
        builder
            .Property(value => value.OfferVersion)
            .HasColumnName("OfferVersion")
            .HasMaxLength(100)
            .IsRequired();
    }

    public static void ConfigurePayment<TOwner>(
        this OwnedNavigationBuilder<TOwner, PaymentReference> builder
    )
        where TOwner : class
    {
        builder.Property(value => value.Source).HasColumnName("PaymentSource").HasMaxLength(100).IsRequired();
        builder.Property(value => value.PaymentId).HasColumnName("RelatedPaymentId").IsRequired();
    }
}
