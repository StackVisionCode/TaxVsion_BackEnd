using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Configurations;

public sealed class FormRequestConfiguration : IEntityTypeConfiguration<FormRequest>
{
    public void Configure(EntityTypeBuilder<FormRequest> builder)
    {
        builder.ToTable(
            "FormRequests",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_FormRequests_Participant",
                    "([ParticipantKind] = 1 AND [CustomerId] IS NOT NULL AND [ParticipantId] IS NULL) "
                        + "OR ([ParticipantKind] = 2 AND [ParticipantId] IS NOT NULL AND [ResponsibleUserId] IS NOT NULL)"
                );
                table.HasCheckConstraint(
                    "CK_FormRequests_CustomerLink",
                    "([ParticipantKind] = 1 AND [CustomerLinkStatus] = 0 AND [LinkBlockedCustomerId] IS NULL) "
                        + "OR ([ParticipantKind] = 2 AND [CustomerLinkStatus] = 0 AND [CustomerId] IS NULL AND [LinkBlockedCustomerId] IS NULL) "
                        + "OR ([ParticipantKind] = 2 AND [CustomerLinkStatus] = 1 AND [CustomerId] IS NOT NULL AND [LinkBlockedCustomerId] IS NULL) "
                        + "OR ([ParticipantKind] = 2 AND [CustomerLinkStatus] = 2 AND [CustomerId] IS NULL AND [LinkBlockedCustomerId] IS NOT NULL)"
                );
            }
        );
        builder.HasKey(request => request.Id);
        builder.Property(request => request.Id).ValueGeneratedNever();
        builder.Property(request => request.TenantId).IsRequired();
        builder.Property(request => request.ParticipantKind).HasConversion<int>().IsRequired();
        builder.Property(request => request.CustomerLinkStatus).HasConversion<int>().IsRequired();
        builder.Property(request => request.LinkBlockedReason).HasMaxLength(64);
        builder.Property(request => request.Origin).HasConversion<int>().IsRequired();
        builder.Property(request => request.Title).HasMaxLength(200).IsRequired();
        builder.Property(request => request.MessageToClient).HasMaxLength(2000);
        builder.Property(request => request.Status).HasConversion<int>().IsRequired();
        builder.Property(request => request.CompletionNote).HasMaxLength(2000);
        builder.Property(request => request.CancelReason).HasConversion<int?>();
        builder.Property(request => request.CancelNote).HasMaxLength(2000);
        builder.Property(request => request.ProfileUpdateStatus).HasConversion<int>().IsRequired();
        builder.Property(request => request.RowVersion).IsRowVersion();
        builder.Ignore(request => request.Participant);
        builder.Ignore(request => request.CustomerLink);
        builder.Ignore(request => request.EffectiveCustomerId);

        builder.HasOne<FormTemplateVersion>()
            .WithMany()
            .HasForeignKey(request => request.FormTemplateVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(request => request.Submission)
            .WithOne()
            .HasForeignKey<FormSubmission>(submission => submission.FormRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(request => new { request.TenantId, request.CustomerId, request.Status })
            .HasFilter("[CustomerId] IS NOT NULL")
            .HasDatabaseName("IX_FormRequests_TenantId_CustomerId_Status");
        builder.HasIndex(request => new { request.TenantId, request.ParticipantId, request.Status })
            .HasFilter("[ParticipantId] IS NOT NULL")
            .HasDatabaseName("IX_FormRequests_TenantId_ParticipantId_Status");
        builder.HasIndex(request => new { request.TenantId, request.ResponsibleUserId, request.Status })
            .HasDatabaseName("IX_FormRequests_TenantId_ResponsibleUserId_Status");
        builder.HasIndex(request => new { request.TenantId, request.Status, request.DueAtUtc })
            .HasDatabaseName("IX_FormRequests_TenantId_Status_DueAtUtc");
        builder.HasIndex(request => new { request.TenantId, request.FormTemplateId })
            .HasDatabaseName("IX_FormRequests_TenantId_FormTemplateId");
        builder.HasIndex(request => new { request.TenantId, request.CustomerId, request.FormTemplateId })
            .IsUnique()
            .HasFilter("[Status] IN (1, 2, 3) AND [CustomerId] IS NOT NULL")
            .HasDatabaseName("UX_FormRequests_OpenPerCustomerTemplate");
        builder.HasIndex(request => new { request.TenantId, request.ParticipantId, request.FormTemplateId })
            .IsUnique()
            .HasFilter("[Status] IN (1, 2, 3) AND [CustomerId] IS NULL AND [ParticipantId] IS NOT NULL")
            .HasDatabaseName("UX_FormRequests_OpenPerParticipantTemplate");
    }
}
