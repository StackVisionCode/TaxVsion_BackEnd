using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.CloudStorage.Domain.FileTypes;

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Configurations;

// Catálogo de tipos y políticas por carpeta: de la plataforma (BaseEntity), sin filtro de tenant.
public sealed class FileTypeDefinitionConfiguration : IEntityTypeConfiguration<FileTypeDefinition>
{
    public void Configure(EntityTypeBuilder<FileTypeDefinition> builder)
    {
        builder.ToTable("FileTypeDefinitions");
        builder.HasKey(definition => definition.Id);
        builder.Property(definition => definition.Id).ValueGeneratedNever();
        builder.Property(definition => definition.Extension).HasMaxLength(16).IsRequired();
        builder.Property(definition => definition.Group).HasMaxLength(40).IsRequired();
        builder
            .Property(definition => definition.ContentTypes)
            .HasMaxLength(FileTypeDefinition.MaxContentTypesLength)
            .IsRequired();
        builder.Property(definition => definition.IsEnabled).IsRequired();
        builder.Property(definition => definition.UpdatedAtUtc).IsRequired();
        builder.Ignore(definition => definition.ContentTypeList);
        builder.HasIndex(definition => definition.Extension).IsUnique();
    }
}

public sealed class FolderFilePolicyConfiguration : IEntityTypeConfiguration<FolderFilePolicy>
{
    public void Configure(EntityTypeBuilder<FolderFilePolicy> builder)
    {
        builder.ToTable("FolderFilePolicies");
        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.Id).ValueGeneratedNever();
        builder.Property(policy => policy.FolderType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(policy => policy.MaxSizeBytes).IsRequired();
        builder
            .Property(policy => policy.AllowedExtensions)
            .HasMaxLength(FolderFilePolicy.MaxExtensionsLength)
            .IsRequired();
        builder.Property(policy => policy.UsesPlanFileTypes).IsRequired();
        builder.Property(policy => policy.UsesPlanFileSize).IsRequired();
        builder.Property(policy => policy.UpdatedAtUtc).IsRequired();
        builder.Ignore(policy => policy.ExtensionList);
        builder.HasIndex(policy => policy.FolderType).IsUnique();
    }
}

public sealed class FilePolicyAuditConfiguration : IEntityTypeConfiguration<FilePolicyAudit>
{
    public void Configure(EntityTypeBuilder<FilePolicyAudit> builder)
    {
        builder.ToTable("FilePolicyAudits");
        builder.HasKey(audit => audit.Id);
        builder.Property(audit => audit.Id).ValueGeneratedNever();
        builder.Property(audit => audit.Kind).HasMaxLength(32).IsRequired();
        builder.Property(audit => audit.Key).HasMaxLength(64).IsRequired();
        builder.Property(audit => audit.Before).HasMaxLength(FilePolicyAudit.MaxSnapshotLength);
        builder.Property(audit => audit.After).HasMaxLength(FilePolicyAudit.MaxSnapshotLength).IsRequired();
        builder.Property(audit => audit.AtUtc).IsRequired();
        builder.HasIndex(audit => audit.AtUtc);
    }
}
