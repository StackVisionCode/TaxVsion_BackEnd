using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Configurations;

/// <summary>
/// <see cref="Draft.Attachments"/>/<see cref="Draft.ReplyContext"/> van en columnas JSON, no
/// child tables — mismo patrón que <c>SentMessageConfiguration</c> en Postmaster (D3 Compose),
/// por consistencia entre los dos servicios para el mismo concepto (plan §23): un
/// <see cref="DraftAttachmentRef"/>/<see cref="Compose.ReplyContext"/> es un valor inmutable sin
/// ciclo de vida propio, a diferencia de <see cref="DraftRecipient"/> (child table real, ver
/// <see cref="DraftRecipientConfiguration"/>) o de <see cref="Inbox.IncomingEmailAttachment"/>
/// (que sí tiene su propio ciclo de vida de descarga).
/// </summary>
internal sealed class DraftConfiguration : IEntityTypeConfiguration<Draft>
{
    private sealed record AttachmentDto(Guid FileId, string Filename, string ContentType, long SizeBytes);

    private sealed record ReplyContextDto(
        Guid IncomingEmailId,
        Guid EmailThreadId,
        string? InReplyToInternetMessageId,
        List<string>? References,
        string? ReplyToProviderMessageId
    );

    public void Configure(EntityTypeBuilder<Draft> builder)
    {
        builder.ToTable("Drafts");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.TenantId).IsRequired();
        builder.Property(d => d.CustomerId).IsRequired();
        builder.Property(d => d.AccountId).IsRequired();
        builder.Property(d => d.Subject).IsRequired().HasMaxLength(Draft.SubjectMaxLength);
        builder.Property(d => d.HtmlBody).IsRequired().HasColumnType("nvarchar(max)");
        builder.Property(d => d.TextBody).HasColumnType("nvarchar(max)");
        builder.Property(d => d.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(d => d.SentMessageId);
        builder.Property(d => d.FailureReason).HasMaxLength(Draft.FailureReasonMaxLength);
        builder.Property(d => d.CreatedAtUtc).IsRequired();
        builder.Property(d => d.UpdatedAtUtc).IsRequired();
        builder.Property(d => d.LastAutoSavedAtUtc);

        // Attachments es una propiedad computada de solo lectura sobre el backing field _attachments
        // — EF la descubriría por convención como navigation collection si no se ignora
        // explícitamente (mismo problema que Postmaster ya resolvió en SentMessageConfiguration).
        builder.Ignore(d => d.Attachments);

        var attachmentsConverter = new ValueConverter<List<DraftAttachmentRef>, string>(
            attachments => JsonSerializer.Serialize(ToDtos(attachments), (JsonSerializerOptions?)null),
            json => ParseAttachments(json)
        );
        var attachmentsComparer = new ValueComparer<List<DraftAttachmentRef>>(
            (a, b) => (a ?? new()).Select(x => x.FileId).SequenceEqual((b ?? new()).Select(x => x.FileId)),
            list => list.Aggregate(0, (hash, attachment) => HashCode.Combine(hash, attachment.FileId)),
            list => list.ToList()
        );
        builder
            .Property<List<DraftAttachmentRef>>("_attachments")
            .HasColumnName("AttachmentsJson")
            .HasConversion(attachmentsConverter)
            .HasColumnType("nvarchar(max)")
            .Metadata.SetValueComparer(attachmentsComparer);

        // ReplyContext tiene setter privado (no es un wrapper de solo lectura sobre un backing
        // field, a diferencia de Attachments) — EF puede mapearlo directamente vía Property() +
        // conversion, sin necesitar el truco del backing field.
        var replyContextConverter = new ValueConverter<ReplyContext?, string?>(
            context => context == null ? null : JsonSerializer.Serialize(ToDto(context), (JsonSerializerOptions?)null),
            json => string.IsNullOrEmpty(json) ? null : ParseReplyContext(json)
        );
        var replyContextComparer = new ValueComparer<ReplyContext?>(
            (a, b) => (a == null && b == null) || (a != null && b != null && a.IncomingEmailId == b.IncomingEmailId),
            context => context == null ? 0 : context.IncomingEmailId.GetHashCode(),
            context => context
        );
        builder
            .Property(d => d.ReplyContext)
            .HasColumnName("ReplyContextJson")
            .HasConversion(replyContextConverter)
            .HasColumnType("nvarchar(max)")
            .Metadata.SetValueComparer(replyContextComparer);

        // Columna real (no solo dentro de ReplyContextJson) — ver el WHY-comment de
        // Draft.EmailThreadId sobre por qué esta Fase 15 se desvía del precedente de Fase 10
        // (FindOpenReplyDraftAsync, que sí filtra el JSON en memoria).
        builder.Property(d => d.EmailThreadId);

        builder.HasMany(d => d.Recipients).WithOne().HasForeignKey(r => r.DraftId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Draft.Recipients))!.SetPropertyAccessMode(PropertyAccessMode.Field);

        // "Retomar borradores" — drafts abiertos de un customer, más reciente primero (plan §24).
        builder
            .HasIndex(d => new
            {
                d.TenantId,
                d.CustomerId,
                d.Status,
                d.UpdatedAtUtc,
            })
            .IsDescending(false, false, false, true)
            .HasDatabaseName("IX_Drafts_TenantId_CustomerId_Status_UpdatedAtUtc");

        // Job de limpieza de drafts abandonados (Fase 16, plan §24/§30).
        builder.HasIndex(d => new { d.Status, d.UpdatedAtUtc }).HasDatabaseName("IX_Drafts_Status_UpdatedAtUtc");

        // Fase 15 — "Drafts Sent de este hilo" para el thread unificado (ListThreadMessagesHandler).
        // Ver el WHY-comment de Draft.EmailThreadId: a diferencia de
        // IX_Drafts_TenantId_CustomerId_Status_UpdatedAtUtc (acota "abiertos", conjunto que nunca
        // crece sin límite), este índice existe porque "todos los Sent de un customer" sí crece sin
        // límite, y acá además filtramos por hilo puntual, no por customer entero.
        builder
            .HasIndex(d => new
            {
                d.TenantId,
                d.EmailThreadId,
                d.Status,
            })
            .HasDatabaseName("IX_Drafts_TenantId_EmailThreadId_Status");
    }

    private static List<AttachmentDto> ToDtos(List<DraftAttachmentRef> attachments) =>
        attachments.Select(a => new AttachmentDto(a.FileId, a.Filename, a.ContentType, a.SizeBytes)).ToList();

    private static List<DraftAttachmentRef> ParseAttachments(string json)
    {
        if (string.IsNullOrEmpty(json))
            return [];

        var dtos = JsonSerializer.Deserialize<List<AttachmentDto>>(json, (JsonSerializerOptions?)null) ?? [];
        return dtos.Select(dto =>
                DraftAttachmentRef.Create(dto.FileId, dto.Filename, dto.ContentType, dto.SizeBytes).Value
            )
            .ToList();
    }

    private static ReplyContextDto ToDto(ReplyContext context) =>
        new(
            context.IncomingEmailId,
            context.EmailThreadId,
            context.InReplyToInternetMessageId,
            context.References.Count == 0 ? null : context.References.ToList(),
            context.ReplyToProviderMessageId
        );

    private static ReplyContext ParseReplyContext(string json)
    {
        var dto = JsonSerializer.Deserialize<ReplyContextDto>(json, (JsonSerializerOptions?)null)!;
        return ReplyContext
            .Create(
                dto.IncomingEmailId,
                dto.EmailThreadId,
                dto.InReplyToInternetMessageId,
                dto.References,
                dto.ReplyToProviderMessageId
            )
            .Value;
    }
}
