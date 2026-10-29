using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Configurations;

public sealed class FormSubmissionRevisionConfiguration : IEntityTypeConfiguration<FormSubmissionRevision>
{
    public void Configure(EntityTypeBuilder<FormSubmissionRevision> builder)
    {
        builder.ToTable(
            "FormSubmissionRevisions",
            table =>
                table.HasCheckConstraint(
                    "CK_FormSubmissionRevisions_SubmittedBy",
                    "([SubmittedByKind] = N'PortalUser' AND [SubmittedByPortalUserId] IS NOT NULL AND [SubmittedByParticipantId] IS NULL AND [SubmittedBySessionId] IS NULL) OR "
                        + "([SubmittedByKind] = N'ExternalSession' AND [SubmittedByPortalUserId] IS NULL AND [SubmittedByParticipantId] IS NOT NULL AND [SubmittedBySessionId] IS NOT NULL)"
                )
        );
        builder.HasKey(revision => revision.Id);
        builder.Property(revision => revision.Id).ValueGeneratedNever();
        builder.Property(revision => revision.Outcome).HasConversion<int>().IsRequired();
        builder.Property(revision => revision.ReviewMessage).HasMaxLength(2000);
        builder.Property(revision => revision.CompletionNote).HasMaxLength(2000);
        builder.Property(revision => revision.PdfStatus).HasConversion<int>().IsRequired();

        // P-18 — estado y referencias de la firma del Submission Summary.
        builder.Property(revision => revision.SignatureStatus).HasConversion<int>().IsRequired();
        builder.Property(revision => revision.SignatureRequestId);
        builder.Property(revision => revision.SignatureCopyFileId);
        builder.Property(revision => revision.DocumentHashPre).HasMaxLength(64);
        builder.Property(revision => revision.DocumentHashPost).HasMaxLength(64);
        builder.Property(revision => revision.SealedFileId);
        builder.Property(revision => revision.SignatureCertificateFileId);

        builder
            .HasIndex(revision => new { revision.FormSubmissionId, revision.RevisionNumber })
            .IsUnique()
            .HasDatabaseName("UX_FormSubmissionRevisions_SubmissionId_Number");
        builder
            .HasIndex(revision => revision.PdfGenerationId)
            .HasDatabaseName("IX_FormSubmissionRevisions_PdfGenerationId");
        builder
            .HasOne<FormSubmission>()
            .WithMany(submission => submission.Revisions)
            .HasForeignKey(revision => revision.FormSubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.OwnsOne(
            revision => revision.Answers,
            owned =>
            {
                owned
                    .Property(value => value.Cipher)
                    .HasColumnName("AnswersCipher")
                    .HasColumnType("nvarchar(max)")
                    .IsRequired();
                owned.Property(value => value.KeyVersion).HasColumnName("AnswersKeyVersion").IsRequired();
            }
        );
        builder.OwnsOne(
            revision => revision.SubmittedBy,
            owned =>
            {
                owned
                    .Property(actor => actor.Kind)
                    .HasConversion<string>()
                    .HasMaxLength(24)
                    .HasColumnName("SubmittedByKind")
                    .IsRequired();
                owned.Property(actor => actor.PortalUserId).HasColumnName("SubmittedByPortalUserId");
                owned.Property(actor => actor.ParticipantId).HasColumnName("SubmittedByParticipantId");
                owned.Property(actor => actor.SessionId).HasColumnName("SubmittedBySessionId");
            }
        );
        builder.Navigation(revision => revision.FieldComments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
