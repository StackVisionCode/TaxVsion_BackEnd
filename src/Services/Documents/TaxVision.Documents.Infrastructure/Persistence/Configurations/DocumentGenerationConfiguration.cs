using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Documents.Domain.Generations;

namespace TaxVision.Documents.Infrastructure.Persistence.Configurations;

public sealed class DocumentGenerationConfiguration : IEntityTypeConfiguration<DocumentGeneration>
{
    public void Configure(EntityTypeBuilder<DocumentGeneration> builder)
    {
        builder.ToTable("DocumentGenerations", DocumentsSchemas.Documents);
        builder.HasKey(g => g.Id);

        builder.Property(g => g.TenantId).IsRequired();

        builder
            .Property(g => g.DocumentType)
            .HasConversion(v => v.Value, v => TaxVision.Documents.Domain.ValueObjects.DocumentType.Create(v).Value!)
            .HasColumnName("DocumentType")
            .HasMaxLength(60)
            .IsRequired();
        builder
            .Property(g => g.TemplateKey)
            .HasConversion(v => v.Value, v => TaxVision.Documents.Domain.ValueObjects.TemplateKey.Create(v).Value!)
            .HasColumnName("TemplateKey")
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(g => g.TemplateVersion).IsRequired();
        builder.Property(g => g.OutputFormat).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(g => g.SourceService).HasMaxLength(60).IsRequired();
        builder.Property(g => g.DocumentVersion).IsRequired();
        builder.Property(g => g.Priority).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(g => g.Status).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.OwnsOne(
            g => g.Owner,
            owner =>
            {
                owner.Property(o => o.OwnerType).HasColumnName("OwnerType").HasMaxLength(60).IsRequired();
                owner.Property(o => o.OwnerId).HasColumnName("OwnerId").IsRequired();
            }
        );
        builder.Navigation(g => g.Owner).IsRequired();

        builder.OwnsOne(
            g => g.Storage,
            storage =>
            {
                storage.Property(s => s.FileId).HasColumnName("StorageFileId");
                storage.Property(s => s.ContentType).HasColumnName("StorageContentType").HasMaxLength(255);
                storage.Property(s => s.SizeBytes).HasColumnName("StorageSizeBytes");
                storage.Property(s => s.ChecksumSha256).HasColumnName("StorageChecksumSha256").HasMaxLength(64);
            }
        );

        builder.Property(g => g.FileId).HasColumnName("FileId");
        builder.Property(g => g.FileName).HasMaxLength(255);
        builder
            .Property(g => g.ContentHash)
            .HasConversion(
                v => v == null ? null : v.Value,
                v => v == null ? null : TaxVision.Documents.Domain.ValueObjects.ContentHash.Create(v).Value
            )
            .HasColumnName("ContentHash")
            .HasColumnType("char(64)")
            .IsFixedLength();

        builder.Property(g => g.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(g => g.AttemptCount).IsRequired();
        builder.Property(g => g.ErrorCode).HasMaxLength(100);
        builder.Property(g => g.ErrorMessage).HasMaxLength(2000);
        builder.Property(g => g.CorrelationId).HasMaxLength(200);
        builder.Property(g => g.CausationId).HasMaxLength(200);

        builder.Property(g => g.RequestedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(g => g.StartedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(g => g.CompletedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(g => g.UpdatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(g => g.RowVersion).IsRowVersion();

        // Idempotencia de la solicitud: una generación por (TenantId, SourceService, IdempotencyKey).
        builder
            .HasIndex(g => new { g.TenantId, g.SourceService, g.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_DocumentGenerations_Tenant_Source_IdempotencyKey");

        // Correlación del evento CloudStorage FileAvailable (por el FileId que subió Documents).
        builder
            .HasIndex(g => g.FileId)
            .HasFilter("[FileId] IS NOT NULL")
            .HasDatabaseName("IX_DocumentGenerations_FileId");

        builder.HasIndex(g => new { g.TenantId, g.Status }).HasDatabaseName("IX_DocumentGenerations_Tenant_Status");
    }
}
