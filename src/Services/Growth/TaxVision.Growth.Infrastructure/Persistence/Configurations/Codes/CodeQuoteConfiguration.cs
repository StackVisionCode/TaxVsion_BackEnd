using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.Quotes;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Codes;

public sealed class CodeQuoteConfiguration : IEntityTypeConfiguration<CodeQuote>
{
    public void Configure(EntityTypeBuilder<CodeQuote> builder)
    {
        builder.ToTable(
            "CodeQuotes",
            GrowthSchemas.Codes,
            table =>
                table.HasCheckConstraint(
                    "CK_CodeQuotes_Amounts",
                    "[GrossAmountCents] >= 0 AND [DiscountAmountCents] >= 0 "
                        + "AND [NetAmountCents] >= 0 "
                        + "AND [GrossAmountCents] = [DiscountAmountCents] + [NetAmountCents]"
                )
        );
        builder.HasKey(quote => quote.Id);

        builder.Property(quote => quote.TenantId).IsRequired();
        builder
            .Property(quote => quote.SnapshotHash)
            .HasConversion(hash => hash.Value, value => SnapshotHash.Create(value).Value)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder
            .Property(quote => quote.IdempotencyKey)
            .HasConversion(key => key.Value, value => IdempotencyKey.Create(value).Value)
            .HasMaxLength(200)
            .IsRequired();
        builder
            .Property(quote => quote.PayloadFingerprint)
            .HasConversion(value => value.Value, value => PayloadFingerprint.Create(value).Value)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(quote => quote.CreatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(quote => quote.ExpiresAtUtc).HasColumnType("datetime2(7)").IsRequired();

        builder.OwnsOne(quote => quote.CodeDisplay, display => display.ConfigureDisplay("Code"));
        builder.Navigation(quote => quote.CodeDisplay).IsRequired();
        builder.OwnsOne(quote => quote.Subject, subject => subject.ConfigureSubject());
        builder.Navigation(quote => quote.Subject).IsRequired();
        builder.OwnsOne(quote => quote.Offer, offer => offer.ConfigureOffer());
        builder.Navigation(quote => quote.Offer).IsRequired();
        builder.OwnsOne(quote => quote.GrossAmount, money => money.ConfigureMoney("Gross"));
        builder.Navigation(quote => quote.GrossAmount).IsRequired();
        builder.OwnsOne(quote => quote.DiscountAmount, money => money.ConfigureMoney("Discount"));
        builder.Navigation(quote => quote.DiscountAmount).IsRequired();
        builder.OwnsOne(quote => quote.NetAmount, money => money.ConfigureMoney("Net"));
        builder.Navigation(quote => quote.NetAmount).IsRequired();

        builder
            .HasOne<CodeDefinition>()
            .WithMany()
            .HasForeignKey(quote => quote.CodeDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<CodeRuleVersion>()
            .WithMany()
            .HasForeignKey(quote => quote.CodeRuleVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(quote => new { quote.TenantId, quote.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_CodeQuotes_TenantId_IdempotencyKey");
        builder
            .HasIndex(quote => new { quote.TenantId, quote.ExpiresAtUtc })
            .HasDatabaseName("IX_CodeQuotes_TenantId_ExpiresAtUtc");
    }
}
