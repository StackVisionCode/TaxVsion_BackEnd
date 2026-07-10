using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class SignerConfiguration : IEntityTypeConfiguration<Signer>
{
    public void Configure(EntityTypeBuilder<Signer> builder)
    {
        builder.ToTable("Signers");
        builder.HasKey(signer => signer.Id);

        builder.Property(signer => signer.SignatureRequestId).IsRequired();
        builder.Property(signer => signer.MappedCustomerId);
        builder.Property(signer => signer.Order).IsRequired();
        builder.Property(signer => signer.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.OwnsOne(
            signer => signer.Email,
            email =>
            {
                email.Property(e => e.Value).HasColumnName("Email").HasMaxLength(SignerEmail.MaxLength).IsRequired();
                email.HasIndex(e => e.Value);
            }
        );

        builder.OwnsOne(
            signer => signer.FullName,
            fullName =>
            {
                fullName
                    .Property(f => f.Value)
                    .HasColumnName("FullName")
                    .HasMaxLength(SignerFullName.MaxLength)
                    .IsRequired();
            }
        );

        builder.OwnsOne(
            signer => signer.PhoneNumber,
            phone =>
            {
                phone.Property(p => p.Value).HasColumnName("PhoneNumber").HasMaxLength(SignerPhoneNumber.MaxLength);
            }
        );

        builder.Property(signer => signer.SignedAtUtc);
        builder.Property(signer => signer.RejectedAtUtc);
        builder.Property(signer => signer.RejectReason).HasMaxLength(2000);
        builder.Property(signer => signer.ClientIp).HasMaxLength(45);
        builder.Property(signer => signer.UserAgent).HasMaxLength(500);
        builder.Property(signer => signer.HasAcceptedConsent).IsRequired();
        builder.Property(signer => signer.ConsentAcceptedAtUtc);
        builder.Property(signer => signer.FirstViewedAtUtc);
        // SignatureCaptureMethod evidence (Fase 3 residual)
        builder.Property(signer => signer.CaptureMethod).HasConversion<string>().HasMaxLength(16);
        builder.Property(signer => signer.TypedName).HasMaxLength(SignerFullName.MaxLength);
        builder.Property(signer => signer.SignatureImageFileId);
        builder.Property(signer => signer.IsPinVerified).IsRequired();
        builder.Property(signer => signer.PinVerifiedAtUtc);
        builder.Property(signer => signer.PinFailedAttempts).IsRequired();
        builder.Property(signer => signer.PinLockedUntilUtc);

        // Orden único dentro de una solicitud (soporta cambios de orden via renumeración).
        // La regla P-11 (email único por solicitud) la garantiza el aggregate; el respaldo
        // en BD llega en una migration posterior con un unique index computado sobre el
        // shadow property del owned Email.
        builder.HasIndex(signer => new { signer.SignatureRequestId, signer.Order }).IsUnique();

        builder
            .HasMany(signer => signer.Fields)
            .WithOne()
            .HasForeignKey(field => field.SignerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Signer.Fields))!.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder
            .HasMany(signer => signer.Challenges)
            .WithOne()
            .HasForeignKey(c => c.SignerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Signer.Challenges))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
