using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Configurations;

public sealed class FormFieldCommentConfiguration : IEntityTypeConfiguration<FormFieldComment>
{
    public void Configure(EntityTypeBuilder<FormFieldComment> builder)
    {
        builder.ToTable("FormFieldComments");
        builder.HasKey(comment => comment.Id);
        builder.Property(comment => comment.Id).ValueGeneratedNever();
        builder.Property(comment => comment.ComponentId).HasMaxLength(32).IsRequired();
        builder.Property(comment => comment.RowId).HasMaxLength(32);
        builder.Property(comment => comment.Comment).HasMaxLength(1000).IsRequired();
        builder
            .HasOne<FormSubmissionRevision>()
            .WithMany(revision => revision.FieldComments)
            .HasForeignKey(comment => comment.RevisionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class FormAttachmentConfiguration : IEntityTypeConfiguration<FormAttachment>
{
    public void Configure(EntityTypeBuilder<FormAttachment> builder)
    {
        builder.ToTable("FormAttachments");
        builder.HasKey(attachment => attachment.Id);
        builder.Property(attachment => attachment.Id).ValueGeneratedNever();
        builder.Property(attachment => attachment.TenantId).IsRequired();
        builder.Property(attachment => attachment.ComponentId).HasMaxLength(32).IsRequired();
        builder.Property(attachment => attachment.RowId).HasMaxLength(32);
        builder.Property(attachment => attachment.DisplayName).HasMaxLength(255).IsRequired();
        builder.Property(attachment => attachment.ContentType).HasMaxLength(128).IsRequired();
        builder.Property(attachment => attachment.Status).HasConversion<int>().IsRequired();
        builder.Property(attachment => attachment.RejectionCode).HasMaxLength(32);
        builder.Property(attachment => attachment.OwnerReassignment).HasMaxLength(24);
        builder.Ignore(attachment => attachment.Address);
        builder.Ignore(attachment => attachment.OwnerReassignmentSettled);
        builder
            .HasOne<FormSubmission>()
            .WithMany(submission => submission.Attachments)
            .HasForeignKey(attachment => attachment.FormSubmissionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasIndex(attachment => new { attachment.TenantId, attachment.FileId })
            .IsUnique()
            .HasFilter("[Status] <> 4")
            .HasDatabaseName("UX_FormAttachments_Tenant_File_Active");
        builder.HasIndex(attachment => attachment.FileId).HasDatabaseName("IX_FormAttachments_FileId");
        // La reconciliación busca los pendientes más viejos que el corte.
        builder
            .HasIndex(attachment => new { attachment.Status, attachment.RegisteredAtUtc })
            .HasDatabaseName("IX_FormAttachments_Status_RegisteredAtUtc");
    }
}

public sealed class FormSensitiveAnswerConfiguration : IEntityTypeConfiguration<FormSensitiveAnswer>
{
    public void Configure(EntityTypeBuilder<FormSensitiveAnswer> builder)
    {
        builder.ToTable(
            "FormSensitiveAnswers",
            table =>
                table.HasCheckConstraint(
                    "CK_FormSensitiveAnswers_CapturedBy",
                    "([CapturedByKind] = N'PortalUser' AND [CapturedByPortalUserId] IS NOT NULL AND [CapturedByParticipantId] IS NULL AND [CapturedBySessionId] IS NULL) OR "
                        + "([CapturedByKind] = N'ExternalSession' AND [CapturedByPortalUserId] IS NULL AND [CapturedByParticipantId] IS NOT NULL AND [CapturedBySessionId] IS NOT NULL)"
                )
        );
        builder.HasKey(answer => answer.Id);
        builder.Property(answer => answer.Id).ValueGeneratedNever();
        builder.Property(answer => answer.ComponentId).HasMaxLength(32).IsRequired();
        builder.Property(answer => answer.RowId).HasMaxLength(32);
        builder.Property(answer => answer.Kind).HasConversion<int>().IsRequired();
        builder.Ignore(answer => answer.Address);
        builder
            .HasOne<FormSubmission>()
            .WithMany(submission => submission.SensitiveAnswers)
            .HasForeignKey(answer => answer.FormSubmissionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasIndex(answer => new
            {
                answer.FormSubmissionId,
                answer.ComponentId,
                answer.RowId,
            })
            .IsUnique()
            .HasFilter("[SupersededAtUtc] IS NULL")
            .HasDatabaseName("UX_FormSensitiveAnswers_Active");
        builder.OwnsOne(
            answer => answer.Protected,
            owned =>
            {
                owned.Property(value => value.Cipher).HasColumnName("Cipher").HasMaxLength(1024).IsRequired();
                owned.Property(value => value.KeyVersion).HasColumnName("KeyVersion").IsRequired();
                owned.Property(value => value.Last4).HasColumnName("Last4").HasMaxLength(4).IsRequired();
            }
        );
        builder.OwnsOne(
            answer => answer.CapturedBy,
            owned =>
            {
                owned
                    .Property(actor => actor.Kind)
                    .HasConversion<string>()
                    .HasMaxLength(24)
                    .HasColumnName("CapturedByKind")
                    .IsRequired();
                owned.Property(actor => actor.PortalUserId).HasColumnName("CapturedByPortalUserId");
                owned.Property(actor => actor.ParticipantId).HasColumnName("CapturedByParticipantId");
                owned.Property(actor => actor.SessionId).HasColumnName("CapturedBySessionId");
            }
        );
    }
}

public sealed class FormFileScanVerdictConfiguration : IEntityTypeConfiguration<FormFileScanVerdict>
{
    public void Configure(EntityTypeBuilder<FormFileScanVerdict> builder)
    {
        builder.ToTable("FormFileScanVerdicts");
        builder.HasKey(verdict => verdict.Id);
        builder.Property(verdict => verdict.Id).ValueGeneratedNever();
        builder.Property(verdict => verdict.TenantId).IsRequired();
        builder.Property(verdict => verdict.Status).HasConversion<int>().IsRequired();
        builder.Property(verdict => verdict.OwnerType).HasMaxLength(32);
        builder.Property(verdict => verdict.FolderType).HasMaxLength(32);
        builder.Property(verdict => verdict.ContentType).HasMaxLength(128);
        builder.Property(verdict => verdict.OriginalName).HasMaxLength(255);
        builder.HasIndex(verdict => verdict.TenantId).HasDatabaseName("IX_FormFileScanVerdicts_TenantId");
    }
}
