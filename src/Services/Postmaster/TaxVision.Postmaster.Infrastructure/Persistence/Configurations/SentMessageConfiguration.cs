using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Configurations;

public sealed class SentMessageConfiguration : IEntityTypeConfiguration<SentMessage>
{
    private sealed record InlineAssetDto(string ContentId, Guid CloudStorageFileId, string ContentType, long SizeBytes);

    private sealed record OutboundAttachmentRefDto(
        Guid CloudStorageFileId,
        string Filename,
        string ContentType,
        long SizeBytes
    );

    public void Configure(EntityTypeBuilder<SentMessage> builder)
    {
        builder.ToTable("SentMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.TenantId).IsRequired();
        builder.Property(m => m.NotificationLogId);
        builder.Property(m => m.CorrelationId).HasMaxLength(128);
        builder.Property(m => m.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Subject).HasMaxLength(500).IsRequired();
        builder.Property(m => m.FromAddress).HasMaxLength(320).IsRequired();
        builder.Property(m => m.FromDisplayName).HasMaxLength(100);
        builder.Property(m => m.ReplyTo).HasMaxLength(320);
        builder.Property(m => m.Stream).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.ProviderCode).HasMaxLength(50).IsRequired();
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(25).IsRequired();
        builder.Property(m => m.QueuedAtUtc).IsRequired();
        builder.Property(m => m.SentAtUtc);
        builder.Property(m => m.LastEventAtUtc);
        builder.Property(m => m.ErrorReason).HasMaxLength(500);
        builder.Property(m => m.TemplateKey).HasMaxLength(200);
        builder.Property(m => m.RenderedHtmlChecksum).HasMaxLength(64);
        builder.Property(m => m.MimeSize).IsRequired();
        builder.Property(m => m.Metadata).HasColumnType("nvarchar(max)");
        builder.Property(m => m.RequiredProviderScope).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.CorrespondenceDraftId);
        builder.Property(m => m.InReplyToInternetMessageId).HasMaxLength(500);
        builder.Property(m => m.ProviderThreadId).HasMaxLength(500);

        // InlineAssets/Attachments/References son propiedades computadas de solo lectura — EF las
        // descubriría por convención como navigation collections si no se ignoran explícitamente.
        // Los backing fields se mapean aparte como columnas JSON (ver abajo).
        builder.Ignore(m => m.InlineAssets);
        builder.Ignore(m => m.Attachments);
        builder.Ignore(m => m.References);

        var inlineAssetsConverter = new ValueConverter<List<InlineAsset>, string>(
            assets => JsonSerializer.Serialize(assets.Select(ToDto), (JsonSerializerOptions?)null),
            json => ParseInlineAssets(json)
        );
        var inlineAssetsComparer = new ValueComparer<List<InlineAsset>>(
            (a, b) => (a ?? new()).Select(x => x.ContentId).SequenceEqual((b ?? new()).Select(x => x.ContentId)),
            list => list.Aggregate(0, (hash, asset) => HashCode.Combine(hash, asset.ContentId)),
            list => list.ToList()
        );
        builder
            .Property<List<InlineAsset>>("_inlineAssets")
            .HasColumnName("InlineAssetsJson")
            .HasConversion(inlineAssetsConverter)
            .HasColumnType("nvarchar(max)")
            .Metadata.SetValueComparer(inlineAssetsComparer);

        var attachmentsConverter = new ValueConverter<List<OutboundAttachmentRef>, string>(
            attachments => JsonSerializer.Serialize(attachments.Select(ToDto), (JsonSerializerOptions?)null),
            json => ParseAttachments(json)
        );
        var attachmentsComparer = new ValueComparer<List<OutboundAttachmentRef>>(
            (a, b) =>
                (a ?? new())
                    .Select(x => x.CloudStorageFileId)
                    .SequenceEqual((b ?? new()).Select(x => x.CloudStorageFileId)),
            list => list.Aggregate(0, (hash, attachment) => HashCode.Combine(hash, attachment.CloudStorageFileId)),
            list => list.ToList()
        );
        builder
            .Property<List<OutboundAttachmentRef>>("_attachments")
            .HasColumnName("AttachmentsJson")
            .HasConversion(attachmentsConverter)
            .HasColumnType("nvarchar(max)")
            .Metadata.SetValueComparer(attachmentsComparer);

        var referencesConverter = new ValueConverter<List<string>, string>(
            references => JsonSerializer.Serialize(references, (JsonSerializerOptions?)null),
            json =>
                string.IsNullOrEmpty(json)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>()
        );
        var referencesComparer = new ValueComparer<List<string>>(
            (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
            list => list.Aggregate(0, HashCode.Combine),
            list => list.ToList()
        );
        builder
            .Property<List<string>>("_references")
            .HasColumnName("ReferencesJson")
            .HasConversion(referencesConverter)
            .HasColumnType("nvarchar(max)")
            .Metadata.SetValueComparer(referencesComparer);

        builder
            .HasMany(m => m.Recipients)
            .WithOne()
            .HasForeignKey(r => r.SentMessageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.Events).WithOne().HasForeignKey(e => e.SentMessageId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.TenantId, m.QueuedAtUtc });
        builder.HasIndex(m => new { m.TenantId, m.NotificationLogId });
        builder.HasIndex(m => new { m.TenantId, m.CorrespondenceDraftId });
        // IdempotencyKey es NOT NULL (validado en el aggregate) — índice único simple, sin filtro.
        builder.HasIndex(m => new { m.TenantId, m.IdempotencyKey }).IsUnique();
    }

    private static InlineAssetDto ToDto(InlineAsset asset) =>
        new(asset.ContentId, asset.CloudStorageFileId, asset.ContentType, asset.SizeBytes);

    private static List<InlineAsset> ParseInlineAssets(string json)
    {
        if (string.IsNullOrEmpty(json))
            return [];

        var dtos = JsonSerializer.Deserialize<List<InlineAssetDto>>(json, (JsonSerializerOptions?)null) ?? [];
        return dtos.Select(dto =>
                InlineAsset.Create(dto.ContentId, dto.CloudStorageFileId, dto.ContentType, dto.SizeBytes).Value
            )
            .ToList();
    }

    private static OutboundAttachmentRefDto ToDto(OutboundAttachmentRef attachment) =>
        new(attachment.CloudStorageFileId, attachment.Filename, attachment.ContentType, attachment.SizeBytes);

    private static List<OutboundAttachmentRef> ParseAttachments(string json)
    {
        if (string.IsNullOrEmpty(json))
            return [];

        var dtos = JsonSerializer.Deserialize<List<OutboundAttachmentRefDto>>(json, (JsonSerializerOptions?)null) ?? [];
        return dtos.Select(dto =>
                OutboundAttachmentRef.Create(dto.CloudStorageFileId, dto.Filename, dto.ContentType, dto.SizeBytes).Value
            )
            .ToList();
    }
}
