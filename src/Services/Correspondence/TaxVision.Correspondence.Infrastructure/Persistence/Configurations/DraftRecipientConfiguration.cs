using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Delivery;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Configurations;

internal sealed class DraftRecipientConfiguration : IEntityTypeConfiguration<DraftRecipient>
{
    public void Configure(EntityTypeBuilder<DraftRecipient> builder)
    {
        builder.ToTable("DraftRecipients");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.DraftId).IsRequired();
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Address).IsRequired().HasMaxLength(EmailAddress.MaxLength);
        builder.Property(x => x.Type).IsRequired().HasConversion<string>().HasMaxLength(8);
        builder.Property(x => x.DisplayName).HasMaxLength(DraftRecipient.DisplayNameMaxLength);

        // Estado de entrega por destinatario (Postmaster): null = enviado, sin noticias.
        builder.Property(x => x.DeliveryStatus).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.DeliveryCategory).HasMaxLength(DeliveryNotice.CategoryMaxLength);
        builder.Property(x => x.DeliveryUpdatedAtUtc);
    }
}
