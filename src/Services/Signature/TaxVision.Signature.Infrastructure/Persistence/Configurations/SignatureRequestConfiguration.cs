using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class SignatureRequestConfiguration : IEntityTypeConfiguration<SignatureRequest>
{
    public void Configure(EntityTypeBuilder<SignatureRequest> builder)
    {
        builder.ToTable("SignatureRequests");
        builder.HasKey(request => request.Id);

        builder.Property(request => request.TenantId).IsRequired();
        builder.Property(request => request.CreatedByUserId).IsRequired();

        builder.Property(request => request.Title).HasMaxLength(SignatureRequest.MaxTitleLength).IsRequired();
        builder.Property(request => request.Description).HasMaxLength(SignatureRequest.MaxDescriptionLength);

        builder.Property(request => request.Category).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(request => request.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(request => request.OriginalFileId).IsRequired();
        builder.Property(request => request.SealedFileId);
        builder.Property(request => request.CertificateFileId);

        builder.Property(request => request.RequiresSequentialSigning).IsRequired();
        builder.Property(request => request.RequiresConsent).IsRequired();
        builder.Property(request => request.GenerateCertificate).IsRequired();

        builder.Property(request => request.TokenExpirationHours).IsRequired();
        builder.Property(request => request.ExpiresAtUtc).IsRequired();
        builder.Property(request => request.RevocationEpoch).IsRequired();

        builder.Property(request => request.CreatedAtUtc).IsRequired();
        builder.Property(request => request.UpdatedAtUtc).IsRequired();

        // Practitioner PIN — sólo el hash serializado. Sin índices; nunca se busca por PIN.
        builder.Property(request => request.PractitionerPinHash).HasMaxLength(512);
        builder.Property(request => request.PractitionerPinSetByUserId);
        builder.Property(request => request.PractitionerPinSetAtUtc);

        // Preparer identity — owned VO. Compat con Form 8879 §V y POA.
        builder.OwnsOne(
            request => request.Preparer,
            preparer =>
            {
                preparer
                    .Property(p => p.PtinOrEfin)
                    .HasColumnName("Preparer_PtinOrEfin")
                    .HasMaxLength(PreparerInfo.MaxIdentifierLength);
                preparer
                    .Property(p => p.DisplayName)
                    .HasColumnName("Preparer_DisplayName")
                    .HasMaxLength(PreparerInfo.MaxDisplayNameLength);
                preparer
                    .Property(p => p.TitleLabel)
                    .HasColumnName("Preparer_TitleLabel")
                    .HasMaxLength(PreparerInfo.MaxTitleLabelLength);
            }
        );
        builder.Property(request => request.PreparerSignedByUserId);
        builder.Property(request => request.PreparerSignedAtUtc);

        // Fase 5: reminders schedule state.
        builder.Property(request => request.LastReminderSentAtUtc);
        builder.Property(request => request.RemindersSent).IsRequired();

        // Fase 9: legal hold.
        builder.Property(request => request.LegalHold).IsRequired();
        builder.Property(request => request.LegalHoldReason).HasMaxLength(SignatureRequest.MaxLegalHoldReasonLength);
        builder.Property(request => request.LegalHoldPlacedByUserId);
        builder.Property(request => request.LegalHoldPlacedAtUtc);
        builder.Property(request => request.LegalHoldLiftedByUserId);
        builder.Property(request => request.LegalHoldLiftedAtUtc);
        builder.HasIndex(request => new { request.TenantId, request.LegalHold });

        // Value objects opcionales embebidos como columnas propias.
        builder.OwnsOne(
            request => request.DocumentHashPre,
            hash =>
            {
                hash.Property(h => h.Value).HasColumnName("DocumentHashPre").HasMaxLength(DocumentHash.ExpectedLength);
            }
        );
        builder.OwnsOne(
            request => request.DocumentHashPost,
            hash =>
            {
                hash.Property(h => h.Value).HasColumnName("DocumentHashPost").HasMaxLength(DocumentHash.ExpectedLength);
            }
        );

        // Índices críticos para las queries del historial (Fase 5) y schedulers.
        builder.HasIndex(request => new { request.TenantId, request.Status });
        builder.HasIndex(request => new { request.TenantId, request.CreatedAtUtc });
        builder.HasIndex(request => new { request.TenantId, request.ExpiresAtUtc });

        builder
            .HasMany(request => request.Signers)
            .WithOne()
            .HasForeignKey(signer => signer.SignatureRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .Metadata.FindNavigation(nameof(SignatureRequest.Signers))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
