using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Configurations;

internal sealed class UnmatchedIncomingEmailConfiguration : IEntityTypeConfiguration<UnmatchedIncomingEmail>
{
    public void Configure(EntityTypeBuilder<UnmatchedIncomingEmail> builder)
    {
        builder.ToTable("UnmatchedIncomingEmails");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.FromAddress).IsRequired().HasMaxLength(EmailAddress.MaxLength);
        builder.Property(x => x.Subject).IsRequired().HasMaxLength(UnmatchedIncomingEmail.SubjectMaxLength);
        builder
            .Property(x => x.ProviderMessageId)
            .IsRequired()
            .HasMaxLength(UnmatchedIncomingEmail.ProviderMessageIdMaxLength);
        builder.Property(x => x.ReceivedAtUtc).IsRequired();
        builder.Property(x => x.Reason).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.ExpiresAtUtc).IsRequired();

        // Para el futuro job de limpieza (plan §20/§30 "PurgeUnmatched" — no implementado en esta fase).
        builder.HasIndex(x => x.ExpiresAtUtc).HasDatabaseName("IX_UnmatchedIncomingEmails_ExpiresAtUtc");

        // Para inspeccionar cuarentena de seguridad por tenant (AuthenticationFailed) sin escanear toda la tabla.
        builder
            .HasIndex(x => new { x.TenantId, x.Reason })
            .HasDatabaseName("IX_UnmatchedIncomingEmails_TenantId_Reason");
    }
}
