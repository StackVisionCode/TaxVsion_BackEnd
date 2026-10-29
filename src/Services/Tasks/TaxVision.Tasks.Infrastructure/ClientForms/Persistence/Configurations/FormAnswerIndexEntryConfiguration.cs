using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Configurations;

public sealed class FormAnswerIndexEntryConfiguration : IEntityTypeConfiguration<FormAnswerIndexEntry>
{
    public void Configure(EntityTypeBuilder<FormAnswerIndexEntry> builder)
    {
        builder.ToTable(
            "FormAnswerIndexEntries",
            table =>
                // Al menos uno de cliente/participante (patrón P-14.10).
                table.HasCheckConstraint(
                    "CK_FormAnswerIndexEntries_CustomerOrParticipant",
                    "[CustomerId] IS NOT NULL OR [ParticipantId] IS NOT NULL"
                )
        );
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Key).HasMaxLength(FormAnswerIndexEntry.KeyMaxLength).IsRequired();
        builder.Property(entry => entry.ValueText).HasMaxLength(FormAnswerIndexEntry.ValueTextMaxLength).IsRequired();
        builder.Property(entry => entry.ValueNumber).HasColumnType("decimal(19,6)");
        builder.Property(entry => entry.ValueDate).HasColumnType("date");
        builder
            .HasIndex(entry => new
            {
                entry.TenantId,
                entry.FormTemplateId,
                entry.Key,
                entry.ValueText,
            })
            .HasDatabaseName("IX_FormAnswerIndexEntries_TenantId_Template_Key_Value");
        builder
            .HasIndex(entry => new { entry.TenantId, entry.FormRequestId })
            .HasDatabaseName("IX_FormAnswerIndexEntries_TenantId_Request");
    }
}
