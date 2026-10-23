using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Configurations;

public sealed class FormTemplateConfiguration : IEntityTypeConfiguration<FormTemplate>
{
    public void Configure(EntityTypeBuilder<FormTemplate> builder)
    {
        builder.ToTable(
            "FormTemplates",
            table => table.HasCheckConstraint("CK_FormTemplates_Draft_IsJson", "ISJSON([DraftDefinitionJson]) = 1")
        );
        builder.HasKey(template => template.Id);
        builder.Property(template => template.Id).ValueGeneratedNever();
        builder.Property(template => template.TenantId).IsRequired();
        builder
            .Property(template => template.Name)
            .HasConversion(value => value.Value, value => FormTemplateName.Create(value).Value)
            .HasMaxLength(FormTemplateName.MaxLength)
            .IsRequired();
        builder
            .Property(template => template.Description)
            .HasConversion(
                value => value == null ? null : value.Value,
                value => value == null ? null : FormDescription.Create(value).Value
            )
            .HasMaxLength(FormDescription.MaxLength);
        builder.Property(template => template.Purpose).HasConversion<int>().IsRequired();
        builder.Property(template => template.Status).HasConversion<int>().IsRequired();
        builder
            .Property(template => template.Draft)
            .HasConversion(
                value => value.Json,
                value => FormDefinitionDocument.Create(value, FormDefinitionDocument.MaxBytes).Value
            )
            .HasColumnName("DraftDefinitionJson")
            .IsRequired();
        builder
            .Property(template => template.DraftHash)
            .HasConversion(value => value.Value, value => ContentHash.Create(value).Value)
            .HasColumnName("DraftHash")
            .HasMaxLength(64)
            .IsRequired();
        builder
            .Property(template => template.LatestPublishedHash)
            .HasConversion(
                value => value == null ? null : value.Value,
                value => value == null ? null : ContentHash.Create(value).Value
            )
            .HasColumnName("LatestPublishedHash")
            .HasMaxLength(64);
        builder.OwnsOne(
            template => template.LibraryOrigin,
            owned =>
            {
                owned.Property(value => value.LibraryKey).HasColumnName("LibraryOriginKey").HasMaxLength(100);
                owned.Property(value => value.SourceTemplateId).HasColumnName("LibrarySourceTemplateId");
                owned.Property(value => value.SourceVersionNumber).HasColumnName("LibrarySourceVersionNumber");
            }
        );
        builder.Property(template => template.LibraryKey).HasMaxLength(100);
        builder.Property(template => template.IsLibraryListed).HasDefaultValue(false);
        builder.Property(template => template.LatestVersionNumber).HasDefaultValue(0);
        builder.Property(template => template.RowVersion).IsRowVersion();
        builder
            .HasIndex(template => new
            {
                template.TenantId,
                template.Status,
                template.Name,
            })
            .HasDatabaseName("IX_FormTemplates_TenantId_Status_Name");
        builder
            .HasIndex(template => template.LibraryKey)
            .IsUnique()
            .HasFilter("[LibraryKey] IS NOT NULL")
            .HasDatabaseName("UX_FormTemplates_LibraryKey");
    }
}
