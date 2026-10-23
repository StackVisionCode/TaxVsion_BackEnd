using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Configurations;

public sealed class FormTemplateVersionConfiguration : IEntityTypeConfiguration<FormTemplateVersion>
{
    public void Configure(EntityTypeBuilder<FormTemplateVersion> builder)
    {
        builder.ToTable(
            "FormTemplateVersions",
            table =>
                table.HasCheckConstraint("CK_FormTemplateVersions_Definition_IsJson", "ISJSON([DefinitionJson]) = 1")
        );
        builder.HasKey(version => version.Id);
        builder.Property(version => version.Id).ValueGeneratedNever();
        builder.Property(version => version.TenantId).IsRequired();
        builder.Property(version => version.Title).HasMaxLength(200).IsRequired();
        builder.Property(version => version.Purpose).HasConversion<int>().IsRequired();
        builder.Property(version => version.Presentation).HasConversion<int>().IsRequired();
        builder
            .Property(version => version.Definition)
            .HasConversion(
                value => value.Json,
                value => FormDefinitionDocument.Create(value, FormDefinitionDocument.MaxBytes).Value
            )
            .HasColumnName("DefinitionJson")
            .IsRequired();
        builder
            .Property(version => version.ContentHash)
            .HasConversion(value => value.Value, value => ContentHash.Create(value).Value)
            .HasColumnName("ContentHash")
            .HasMaxLength(64)
            .IsRequired();
        builder
            .HasOne<FormTemplate>()
            .WithMany()
            .HasForeignKey(version => version.FormTemplateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasIndex(version => new { version.FormTemplateId, version.VersionNumber })
            .IsUnique()
            .HasDatabaseName("UX_FormTemplateVersions_TemplateId_VersionNumber");
        builder
            .HasIndex(version => new { version.TenantId, version.FormTemplateId })
            .HasDatabaseName("IX_FormTemplateVersions_TenantId_TemplateId");
    }
}
