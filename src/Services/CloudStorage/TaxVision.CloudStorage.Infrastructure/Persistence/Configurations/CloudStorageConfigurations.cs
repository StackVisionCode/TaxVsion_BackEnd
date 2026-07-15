using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Domain.Legal;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Domain.Sharing;

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Configurations;

public sealed class FileObjectConfiguration : IEntityTypeConfiguration<FileObject>
{
    public void Configure(EntityTypeBuilder<FileObject> builder)
    {
        builder.ToTable("Files");
        builder.HasKey(file => file.Id);
        builder.Property(file => file.TenantId).IsRequired();
        builder.Property(file => file.OwnerType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(file => file.FolderType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(file => file.ObjectKey).HasMaxLength(1024).IsRequired();
        builder.Property(file => file.OriginalName).HasMaxLength(255).IsRequired();
        builder.Property(file => file.DeclaredContentType).HasMaxLength(128).IsRequired();
        builder.Property(file => file.DetectedContentType).HasMaxLength(128);
        builder.Property(file => file.ChecksumSha256).HasMaxLength(64);
        builder.Property(file => file.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(file => file.ScanReport).HasMaxLength(2048);
        builder.Property(file => file.MultipartUploadId).HasMaxLength(1024);
        builder.HasIndex(file => new { file.TenantId, file.Id });
        builder.HasIndex(file => new { file.TenantId, file.ObjectKey }).IsUnique();
        builder.HasIndex(file => new
        {
            file.TenantId,
            file.Status,
            file.CreatedAtUtc,
        });
        // Fase C1 — el job de purga de la papelera escanea cross-tenant por vencimiento;
        // la papelera por tenant ya usa el indice {TenantId, Status, CreatedAtUtc} de arriba.
        builder.HasIndex(file => new { file.Status, file.SoftDeleteExpiresAtUtc });
        // Fase C2 — listar el contenido de una carpeta.
        builder.HasIndex(file => new { file.TenantId, file.FolderId });
    }
}

/// <summary>Fase C2 — arbol de carpetas navegables (ver Domain/Folders/Folder.cs).</summary>
public sealed class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    public void Configure(EntityTypeBuilder<Folder> builder)
    {
        builder.ToTable("Folders");
        builder.HasKey(folder => folder.Id);
        builder.Property(folder => folder.TenantId).IsRequired();
        builder.Property(folder => folder.OwnerType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(folder => folder.Name).HasMaxLength(255).IsRequired();
        builder.Property(folder => folder.RelativePath).HasMaxLength(2048).IsRequired();

        // Listar subcarpetas de un padre (raiz = ParentFolderId null).
        builder.HasIndex(folder => new
        {
            folder.TenantId,
            folder.ParentFolderId,
            folder.Name,
        });
        // Cascadear rename/move a todo el subarbol via prefijo de RelativePath.
        builder.HasIndex(folder => new { folder.TenantId, folder.RelativePath });
    }
}

public sealed class TenantStorageLimitConfiguration : IEntityTypeConfiguration<TenantStorageLimit>
{
    public void Configure(EntityTypeBuilder<TenantStorageLimit> builder)
    {
        builder.ToTable("TenantStorageLimits");
        builder.HasKey(limit => limit.Id);
        builder.Property(limit => limit.TenantId).IsRequired();
        builder.Property(limit => limit.PlanCode).HasMaxLength(64).IsRequired();
        builder.Property(limit => limit.RowVersion).IsRowVersion();
        builder.HasIndex(limit => limit.TenantId).IsUnique();
    }
}

/// <summary>Fase C3 — links de compartir sobre File (Folder queda para la Fase C4, ver ShareLink.ResourceType).</summary>
public sealed class ShareLinkConfiguration : IEntityTypeConfiguration<ShareLink>
{
    public void Configure(EntityTypeBuilder<ShareLink> builder)
    {
        builder.ToTable("ShareLinks");
        builder.HasKey(link => link.Id);
        builder.Property(link => link.TenantId).IsRequired();
        builder.Property(link => link.ResourceType).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(link => link.TokenHash).HasMaxLength(32).IsRequired();
        builder.Property(link => link.TokenLast4).HasMaxLength(4).IsRequired();
        builder.Property(link => link.Visibility).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(link => link.Permission).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(link => link.PasswordHash).HasMaxLength(256);
        builder.Property(link => link.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(link => link.RowVersion).IsRowVersion();

        // Resolver un token entrante (publico o privado) es siempre por hash, cross-tenant.
        builder.HasIndex(link => link.TokenHash).IsUnique();
        // Listar los links vigentes de un recurso (para su dueno).
        builder.HasIndex(link => new
        {
            link.TenantId,
            link.ResourceId,
            link.ResourceType,
        });

        builder
            .HasMany(link => link.Recipients)
            .WithOne()
            .HasForeignKey(recipient => recipient.ShareLinkId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(ShareLink.Recipients))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ShareRecipientConfiguration : IEntityTypeConfiguration<ShareRecipient>
{
    public void Configure(EntityTypeBuilder<ShareRecipient> builder)
    {
        builder.ToTable("ShareRecipients");
        builder.HasKey(recipient => recipient.Id);
        // Entidad hija agregada via ShareLink.AddXxxRecipient — el Id ya viene
        // seteado (BaseEntity lo genera al construir), asi que EF no debe
        // tratarlo como identity o emite UPDATE en vez de INSERT al agregarla
        // a la coleccion (ver guardrail de persistencia).
        builder.Property(recipient => recipient.Id).ValueGeneratedNever();

        builder.Property(recipient => recipient.ShareLinkId).IsRequired();
        builder.Property(recipient => recipient.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(recipient => recipient.RecipientEmail).HasMaxLength(320);

        builder.HasIndex(recipient => new { recipient.ShareLinkId, recipient.RecipientUserId });
        builder.HasIndex(recipient => new { recipient.ShareLinkId, recipient.RecipientCustomerId });
    }
}

public sealed class StorageAccessLogConfiguration : IEntityTypeConfiguration<StorageAccessLog>
{
    public void Configure(EntityTypeBuilder<StorageAccessLog> builder)
    {
        builder.ToTable("StorageAccessLogs");
        builder.HasKey(log => log.Id);
        builder.Property(log => log.TenantId).IsRequired();
        builder.Property(log => log.Action).HasMaxLength(64).IsRequired();
        builder.Property(log => log.Outcome).HasMaxLength(32).IsRequired();
        builder.Property(log => log.IpAddress).HasMaxLength(64);
        builder.Property(log => log.UserAgent).HasMaxLength(512);
        builder.Property(log => log.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(log => log.Details).HasMaxLength(2048);
        builder.HasIndex(log => new { log.TenantId, log.OccurredAtUtc });
        builder.HasIndex(log => new
        {
            log.TenantId,
            log.FileId,
            log.OccurredAtUtc,
        });
        builder.HasIndex(log => new
        {
            log.TenantId,
            log.ActorId,
            log.OccurredAtUtc,
        });
    }
}

/// <summary>Fase L1.3 — expedientes DMCA (ver Domain/Legal/DmcaNotice.cs).</summary>
public sealed class DmcaNoticeConfiguration : IEntityTypeConfiguration<DmcaNotice>
{
    public void Configure(EntityTypeBuilder<DmcaNotice> builder)
    {
        builder.ToTable("CloudStorageDmcaNotices");
        builder.HasKey(notice => notice.Id);
        builder.Property(notice => notice.TenantId).IsRequired();
        builder.Property(notice => notice.FileId).IsRequired();
        builder.Property(notice => notice.ClaimantName).HasMaxLength(255).IsRequired();
        builder.Property(notice => notice.ClaimantEmail).HasMaxLength(320).IsRequired();
        builder.Property(notice => notice.CopyrightedWorkDescription).HasMaxLength(2048).IsRequired();
        builder.Property(notice => notice.InfringingMaterialDescription).HasMaxLength(2048).IsRequired();
        builder.Property(notice => notice.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(notice => notice.CounterNoticeText).HasMaxLength(4096);
        builder.Property(notice => notice.ResolutionNotes).HasMaxLength(2048);
        builder.HasIndex(notice => new { notice.TenantId, notice.Id });
        // Fase L1.3 — HasActiveNoticeForFileAsync escanea por archivo dentro de un tenant.
        builder.HasIndex(notice => new
        {
            notice.TenantId,
            notice.FileId,
            notice.Status,
        });
    }
}
