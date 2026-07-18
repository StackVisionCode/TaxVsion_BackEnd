using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Configurations;

internal sealed class EmailThreadConfiguration : IEntityTypeConfiguration<EmailThread>
{
    public void Configure(EntityTypeBuilder<EmailThread> builder)
    {
        builder.ToTable("EmailThreads");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.CustomerId).IsRequired();
        builder.Property(x => x.Subject).IsRequired().HasMaxLength(EmailThread.SubjectMaxLength);
        builder.Property(x => x.ProviderThreadId).HasMaxLength(EmailThread.ProviderThreadIdMaxLength);
        builder.Property(x => x.FirstMessageAtUtc).IsRequired();
        builder.Property(x => x.LastMessageAtUtc).IsRequired();
        builder.Property(x => x.MessageCount).IsRequired();
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.ArchivedAtUtc);

        builder
            .HasIndex(x => new { x.TenantId, x.ProviderThreadId })
            .IsUnique()
            .HasFilter("[ProviderThreadId] IS NOT NULL")
            .HasDatabaseName("IX_EmailThreads_TenantId_ProviderThreadId_Unique");

        // LastMessageAtUtc DESC per plan §24: hilos con actividad más reciente primero.
        builder
            .HasIndex(x => new
            {
                x.TenantId,
                x.CustomerId,
                x.LastMessageAtUtc,
            })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_EmailThreads_TenantId_CustomerId_LastMessageAtUtc");
    }
}
