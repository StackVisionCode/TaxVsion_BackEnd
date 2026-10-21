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
            table => table.HasCheckConstraint(
                "CK_FormSubmissionRevisions_SubmittedBy",
                "([SubmittedByKind] = 1 AND [SubmittedByPortalUserId] IS NOT NULL AND [SubmittedByParticipantId] IS NULL AND [SubmittedBySessionId] IS NULL) OR "
                    + "([SubmittedByKind] = 2 AND [SubmittedByPortalUserId] IS NULL AND [SubmittedByParticipantId] IS NOT NULL AND [SubmittedBySessionId] IS NOT NULL)"
            )
        );
        builder.HasKey(revision => revision.Id);
        builder.Property(revision => revision.Id).ValueGeneratedNever();
        builder.Property(revision => revision.Outcome).HasConversion<int>().IsRequired();
        builder.Property(revision => revision.ReviewMessage).HasMaxLength(2000);
        builder.Property(revision => revision.CompletionNote).HasMaxLength(2000);
        builder.Property(revision => revision.PdfStatus).HasConversion<int>().IsRequired();
        builder.HasIndex(revision => new { revision.FormSubmissionId, revision.RevisionNumber })
            .IsUnique()
            .HasDatabaseName("UX_FormSubmissionRevisions_SubmissionId_Number");
        builder.HasIndex(revision => revision.PdfGenerationId).HasDatabaseName("IX_FormSubmissionRevisions_PdfGenerationId");
        builder.HasOne<FormSubmission>()
            .WithMany(submission => submission.Revisions)
            .HasForeignKey(revision => revision.FormSubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.OwnsOne(
            revision => revision.Answers,
            owned =>
            {
                owned.Property(value => value.Cipher).HasColumnName("AnswersCipher").HasColumnType("nvarchar(max)").IsRequired();
                owned.Property(value => value.KeyVersion).HasColumnName("AnswersKeyVersion").IsRequired();
            }
        );
        builder.OwnsOne(
            revision => revision.SubmittedBy,
            owned =>
            {
                owned.Property(actor => actor.Kind).HasConversion<int>().HasColumnName("SubmittedByKind").IsRequired();
                owned.Property(actor => actor.PortalUserId).HasColumnName("SubmittedByPortalUserId");
                owned.Property(actor => actor.ParticipantId).HasColumnName("SubmittedByParticipantId");
                owned.Property(actor => actor.SessionId).HasColumnName("SubmittedBySessionId");
            }
        );
        builder.Navigation(revision => revision.FieldComments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
