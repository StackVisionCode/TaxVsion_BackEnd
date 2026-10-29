using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.ClientForms.Domain.Consents;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Configurations;

public sealed class FormParticipantConfiguration : IEntityTypeConfiguration<FormParticipant>
{
    public void Configure(EntityTypeBuilder<FormParticipant> builder)
    {
        builder.ToTable("FormParticipants");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.TenantId).IsRequired();
        builder.Property(item => item.Email).HasMaxLength(254).IsRequired();
        builder.Property(item => item.FullName).HasMaxLength(200);
        builder.Property(item => item.PhoneE164).HasMaxLength(16);
        builder.Property(item => item.Language).HasMaxLength(10).HasDefaultValue("En").IsRequired();
        builder.Property(item => item.Status).HasConversion<int>().IsRequired();
        builder.Property(item => item.Source).HasConversion<int>().IsRequired();
        builder.Property(item => item.LostReason).HasMaxLength(FormParticipant.LostReasonMaxLength);
        builder.Ignore(item => item.IsLost);
        builder
            .HasIndex(item => new { item.TenantId, item.Email })
            .IsUnique()
            .HasFilter("[LinkedCustomerId] IS NULL")
            .HasDatabaseName("UX_FormParticipants_Tenant_Email_Unlinked");
        builder
            .HasIndex(item => new { item.TenantId, item.LinkedCustomerId })
            .HasFilter("[LinkedCustomerId] IS NOT NULL")
            .HasDatabaseName("IX_FormParticipants_Tenant_LinkedCustomer");
    }
}

public sealed class FormIntakeLinkConfiguration : IEntityTypeConfiguration<FormIntakeLink>
{
    public void Configure(EntityTypeBuilder<FormIntakeLink> builder)
    {
        builder.ToTable("FormIntakeLinks");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.TenantId).IsRequired();
        builder.Property(item => item.Name).HasMaxLength(120).IsRequired();
        builder.Property(item => item.Status).HasConversion<int>().IsRequired();
        builder.Property(item => item.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(item => item.TokenLast4).HasMaxLength(4).IsRequired();
        builder.Property(item => item.TokenCipher).HasMaxLength(2048).IsRequired();
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => item.TokenHash).IsUnique().HasDatabaseName("UX_FormIntakeLinks_TokenHash");
        builder
            .HasIndex(item => new { item.TenantId, item.Status })
            .HasDatabaseName("IX_FormIntakeLinks_Tenant_Status");
        builder
            .HasOne<FormTemplate>()
            .WithMany()
            .HasForeignKey(item => item.FormTemplateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<FormTemplateVersion>()
            .WithMany()
            .HasForeignKey(item => item.FormTemplateVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<ConsentNotice>()
            .WithMany()
            .HasForeignKey(item => item.EntryNoticeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FormAccessGrantConfiguration : IEntityTypeConfiguration<FormAccessGrant>
{
    public void Configure(EntityTypeBuilder<FormAccessGrant> builder)
    {
        builder.ToTable("FormAccessGrants");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.TenantId).IsRequired();
        builder.Property(item => item.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(item => item.TokenLast4).HasMaxLength(4).IsRequired();
        builder.HasIndex(item => item.TokenHash).IsUnique().HasDatabaseName("UX_FormAccessGrants_TokenHash");
        builder
            .HasIndex(item => new { item.TenantId, item.FormRequestId })
            .IsUnique()
            .HasDatabaseName("UX_FormAccessGrants_Tenant_Request");
        builder
            .HasOne<FormRequest>()
            .WithMany()
            .HasForeignKey(item => item.FormRequestId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<ConsentNotice>()
            .WithMany()
            .HasForeignKey(item => item.EntryNoticeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FormAccessChallengeConfiguration : IEntityTypeConfiguration<FormAccessChallenge>
{
    public void Configure(EntityTypeBuilder<FormAccessChallenge> builder)
    {
        builder.ToTable(
            "FormAccessChallenges",
            table =>
                table.HasCheckConstraint(
                    "CK_FormAccessChallenges_Source",
                    "([GrantId] IS NOT NULL AND [IntakeLinkId] IS NULL) OR ([GrantId] IS NULL AND [IntakeLinkId] IS NOT NULL)"
                )
        );
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.TenantId).IsRequired();
        builder.Property(item => item.EmailCipher).HasMaxLength(512).IsRequired();
        builder.Property(item => item.EmailKeyVersion).IsRequired();
        builder.Property(item => item.EmailLookupHash).HasMaxLength(80).IsRequired();
        builder.Property(item => item.CodeHash).HasMaxLength(160).IsRequired();
        builder.Property(item => item.EntryNoticeContentHash).HasMaxLength(64);
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder
            .HasIndex(item => new
            {
                item.TenantId,
                item.EmailLookupHash,
                item.IssuedAtUtc,
            })
            .HasDatabaseName("IX_FormAccessChallenges_Tenant_EmailLookup_Issued");
        builder.HasIndex(item => item.GrantId).HasFilter("[GrantId] IS NOT NULL");
        builder.HasIndex(item => item.IntakeLinkId).HasFilter("[IntakeLinkId] IS NOT NULL");
        builder
            .HasOne<FormAccessGrant>()
            .WithMany()
            .HasForeignKey(item => item.GrantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<FormIntakeLink>()
            .WithMany()
            .HasForeignKey(item => item.IntakeLinkId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<ConsentNotice>()
            .WithMany()
            .HasForeignKey(item => item.EntryNoticeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FormAccessSessionConfiguration : IEntityTypeConfiguration<FormAccessSession>
{
    public void Configure(EntityTypeBuilder<FormAccessSession> builder)
    {
        builder.ToTable("FormAccessSessions");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.TenantId).IsRequired();
        builder.Property(item => item.SessionTokenHash).HasMaxLength(64).IsRequired();
        builder.Property(item => item.ClientIp).HasMaxLength(64).IsRequired();
        builder.Property(item => item.UserAgent).HasMaxLength(512).IsRequired();
        builder.HasIndex(item => item.SessionTokenHash).IsUnique().HasDatabaseName("UX_FormAccessSessions_TokenHash");
        builder
            .HasIndex(item => new
            {
                item.TenantId,
                item.FormRequestId,
                item.ExpiresAtUtc,
            })
            .HasDatabaseName("IX_FormAccessSessions_Tenant_Request_Expires");
        builder
            .HasOne<FormRequest>()
            .WithMany()
            .HasForeignKey(item => item.FormRequestId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<FormParticipant>()
            .WithMany()
            .HasForeignKey(item => item.ParticipantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
