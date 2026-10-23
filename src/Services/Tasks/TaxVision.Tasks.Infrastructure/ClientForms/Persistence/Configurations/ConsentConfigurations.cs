using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.ClientForms.Domain.Consents;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Configurations;

public sealed class ConsentNoticeConfiguration : IEntityTypeConfiguration<ConsentNotice>
{
    public void Configure(EntityTypeBuilder<ConsentNotice> builder)
    {
        builder.ToTable("ConsentNotices");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.Key).HasMaxLength(120).IsRequired();
        builder.Property(item => item.Title).HasMaxLength(200).IsRequired();
        builder.Property(item => item.Purpose).HasConversion<int>().IsRequired();
        builder.Property(item => item.Status).HasConversion<int>().IsRequired();
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder
            .HasIndex(item => new { item.TenantId, item.Key })
            .IsUnique()
            .HasDatabaseName("UX_ConsentNotices_Tenant_Key");
        builder.Navigation(item => item.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ConsentNoticeVersionConfiguration : IEntityTypeConfiguration<ConsentNoticeVersion>
{
    public void Configure(EntityTypeBuilder<ConsentNoticeVersion> builder)
    {
        builder.ToTable(
            "ConsentNoticeVersions",
            table => table.HasCheckConstraint("CK_ConsentNoticeVersions_Body_IsJson", "ISJSON([BodyJson]) = 1")
        );
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.Language).HasMaxLength(10).IsRequired();
        builder.Property(item => item.BodyJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(item => item.TextSnapshot).HasMaxLength(20_000).IsRequired();
        builder.Property(item => item.ContentHash).HasMaxLength(64).IsRequired();
        builder
            .HasIndex(item => new
            {
                item.TenantId,
                item.ConsentNoticeId,
                item.VersionNumber,
            })
            .IsUnique()
            .HasDatabaseName("UX_ConsentNoticeVersions_Tenant_Notice_Version");
        builder
            .HasOne<ConsentNotice>()
            .WithMany(item => item.Versions)
            .HasForeignKey(item => item.ConsentNoticeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FormConsentRecordConfiguration : IEntityTypeConfiguration<FormConsentRecord>
{
    public void Configure(EntityTypeBuilder<FormConsentRecord> builder)
    {
        builder.ToTable(
            "FormConsentRecords",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_FormConsentRecords_Participant",
                    "([ParticipantKind] = 1 AND [CustomerId] IS NOT NULL AND [ParticipantId] IS NULL) OR "
                        + "([ParticipantKind] = 2 AND [CustomerId] IS NULL AND [ParticipantId] IS NOT NULL)"
                );
                table.HasCheckConstraint("CK_FormConsentRecords_NoticeBody_IsJson", "ISJSON([NoticeBodyJson]) = 1");
                table.HasCheckConstraint(
                    "CK_FormConsentRecords_AcceptedBy",
                    "([AcceptedByKind] = N'PortalUser' AND [AcceptedByPortalUserId] IS NOT NULL AND [AcceptedByParticipantId] IS NULL AND [AcceptedBySessionId] IS NULL) OR "
                        + "([AcceptedByKind] = N'ExternalSession' AND [AcceptedByPortalUserId] IS NULL AND [AcceptedByParticipantId] IS NOT NULL AND [AcceptedBySessionId] IS NOT NULL)"
                );
            }
        );
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.ComponentId).HasMaxLength(64).IsRequired();
        builder.Property(item => item.ParticipantKind).HasConversion<int>().IsRequired();
        builder.Property(item => item.NoticeContentHash).HasMaxLength(64).IsRequired();
        builder.Property(item => item.NoticeBodyJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(item => item.TextSnapshot).HasMaxLength(20_000).IsRequired();
        builder.Property(item => item.Language).HasMaxLength(10).IsRequired();
        builder.Property(item => item.AcceptanceText).HasMaxLength(300);
        builder.Property(item => item.Outcome).HasConversion<int>().IsRequired();
        builder.Property(item => item.ClientIp).HasMaxLength(64).IsRequired();
        builder.Property(item => item.UserAgent).HasMaxLength(512).IsRequired();
        builder.OwnsOne(
            item => item.AcceptedBy,
            owned =>
            {
                owned
                    .Property(actor => actor.Kind)
                    .HasConversion<string>()
                    .HasMaxLength(24)
                    .HasColumnName("AcceptedByKind")
                    .IsRequired();
                owned.Property(actor => actor.PortalUserId).HasColumnName("AcceptedByPortalUserId");
                owned.Property(actor => actor.ParticipantId).HasColumnName("AcceptedByParticipantId");
                owned.Property(actor => actor.SessionId).HasColumnName("AcceptedBySessionId");
            }
        );
        builder
            .HasOne<FormRequest>()
            .WithMany()
            .HasForeignKey(item => item.FormRequestId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<ConsentNotice>()
            .WithMany()
            .HasForeignKey(item => item.ConsentNoticeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasIndex(item => new
            {
                item.TenantId,
                item.FormRequestId,
                item.ComponentId,
                item.ParticipantKind,
                item.CustomerId,
                item.ParticipantId,
                item.ConsentNoticeId,
                item.NoticeVersionNumber,
                item.Outcome,
            })
            .IsUnique()
            // Sin filtro: CustomerId o ParticipantId siempre es NULL y el filtro por defecto anularía el índice.
            .HasFilter(null)
            .HasDatabaseName("UX_FormConsentRecords_Evidence");
        builder
            .HasIndex(item => new
            {
                item.TenantId,
                item.FormRequestId,
                item.Outcome,
            })
            .HasDatabaseName("IX_FormConsentRecords_Request_Outcome");
    }
}
