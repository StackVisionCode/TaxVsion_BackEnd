using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Configurations;

public sealed class FormSubmissionConfiguration : IEntityTypeConfiguration<FormSubmission>
{
    public void Configure(EntityTypeBuilder<FormSubmission> builder)
    {
        builder.ToTable(
            "FormSubmissions",
            table => table.HasCheckConstraint(
                "CK_FormSubmissions_DraftUpdatedBy",
                "[DraftUpdatedByKind] IS NULL OR "
                    + "([DraftUpdatedByKind] = 1 AND [DraftUpdatedByPortalUserId] IS NOT NULL AND [DraftUpdatedByParticipantId] IS NULL AND [DraftUpdatedBySessionId] IS NULL) OR "
                    + "([DraftUpdatedByKind] = 2 AND [DraftUpdatedByPortalUserId] IS NULL AND [DraftUpdatedByParticipantId] IS NOT NULL AND [DraftUpdatedBySessionId] IS NOT NULL)"
            )
        );
        builder.HasKey(submission => submission.Id);
        builder.Property(submission => submission.Id).ValueGeneratedNever();
        builder.HasIndex(submission => submission.FormRequestId).IsUnique().HasDatabaseName("UX_FormSubmissions_FormRequestId");

        builder.OwnsOne(
            submission => submission.DraftAnswers,
            owned =>
            {
                owned.Property(value => value.Cipher).HasColumnName("DraftAnswersCipher").HasColumnType("nvarchar(max)");
                owned.Property(value => value.KeyVersion).HasColumnName("DraftAnswersKeyVersion");
            }
        );
        builder.OwnsOne(
            submission => submission.DraftUpdatedBy,
            owned => ConfigureActor(owned, "DraftUpdatedBy")
        );
        builder.Navigation(submission => submission.Revisions).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(submission => submission.Attachments).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(submission => submission.SensitiveAnswers).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureActor(OwnedNavigationBuilder<FormSubmission, FormActor> owned, string prefix)
    {
        owned.Property(actor => actor.Kind).HasConversion<int>().HasColumnName(prefix + "Kind");
        owned.Property(actor => actor.PortalUserId).HasColumnName(prefix + "PortalUserId");
        owned.Property(actor => actor.ParticipantId).HasColumnName(prefix + "ParticipantId");
        owned.Property(actor => actor.SessionId).HasColumnName(prefix + "SessionId");
    }
}
