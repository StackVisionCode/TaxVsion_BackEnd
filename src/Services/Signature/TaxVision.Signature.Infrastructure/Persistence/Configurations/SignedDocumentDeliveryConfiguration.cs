using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Delivery;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class SignedDocumentDeliveryConfiguration : IEntityTypeConfiguration<SignedDocumentDelivery>
{
    public void Configure(EntityTypeBuilder<SignedDocumentDelivery> builder)
    {
        builder.ToTable("SignedDocumentDeliveries");
        builder.HasKey(delivery => delivery.Id);
        builder.Property(delivery => delivery.Id).ValueGeneratedNever();

        builder.Property(delivery => delivery.SignatureRequestId).IsRequired();
        builder.Property(delivery => delivery.SignerId).IsRequired();
        builder.Property(delivery => delivery.RecipientEmail).HasMaxLength(SignedDocumentDelivery.MaxEmailLength).IsRequired();
        builder.Property(delivery => delivery.Purpose).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(delivery => delivery.TokenHash).HasMaxLength(DeliveryToken.HashLength).IsRequired();
        builder.Property(delivery => delivery.ExpiresAtUtc).IsRequired();
        builder.Property(delivery => delivery.CreatedAtUtc).IsRequired();
        builder.Property(delivery => delivery.LastIssuedAtUtc).IsRequired();
        builder.Property(delivery => delivery.IssueCount).IsRequired();
        builder.Property(delivery => delivery.AccessCount).IsRequired();

        // El enlace se resuelve por el hash del secreto (único).
        builder.HasIndex(delivery => delivery.TokenHash).IsUnique();
        builder.HasIndex(delivery => new { delivery.TenantId, delivery.SignatureRequestId });
        // Una sola entrega final por firmante: dos FileAvailable concurrentes no duplican el correo.
        builder
            .HasIndex(delivery => new { delivery.SignatureRequestId, delivery.SignerId, delivery.Purpose })
            .IsUnique()
            .HasFilter("[Purpose] = 'Completed'");

        builder.HasMany(delivery => delivery.Items).WithOne().HasForeignKey(item => item.DeliveryId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(SignedDocumentDelivery.Items))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class DeliveryItemConfiguration : IEntityTypeConfiguration<DeliveryItem>
{
    public void Configure(EntityTypeBuilder<DeliveryItem> builder)
    {
        builder.ToTable("SignedDocumentDeliveryItems");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.FileId).IsRequired();
        builder.Property(item => item.Kind).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(item => item.Title).HasMaxLength(DeliveryItem.MaxTitleLength).IsRequired();
        builder.Property(item => item.SizeBytes).IsRequired();
        builder.Property(item => item.Order).IsRequired();
    }
}
