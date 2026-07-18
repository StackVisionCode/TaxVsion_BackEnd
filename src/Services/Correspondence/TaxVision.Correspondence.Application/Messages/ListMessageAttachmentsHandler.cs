using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Messages;

/// <summary>
/// Lectura de metadata bajo demanda (Fase 7) — HTTP-triggered, no un consumer Wolverine, mismo
/// criterio que <see cref="GetMessageBodyHandler"/> (no empuja correlación). A diferencia de ese
/// handler, esto nunca llama a Connectors ni a CloudStorage: los attachments ya están persistidos
/// como filas propias (<see cref="IncomingEmailAttachment"/>), esto solo los mapea.
/// </summary>
public static class ListMessageAttachmentsHandler
{
    public static async Task<Result<IReadOnlyList<AttachmentSummary>>> Handle(
        ListMessageAttachmentsQuery query,
        IIncomingEmailRepository incomingEmails,
        CancellationToken ct
    )
    {
        var email = await incomingEmails.GetByIdAsync(query.TenantId, query.IncomingEmailId, ct);
        if (email is null)
            return Result.Failure<IReadOnlyList<AttachmentSummary>>(
                new Error("IncomingEmail.NotFound", "The message was not found for this tenant.")
            );

        IReadOnlyList<AttachmentSummary> summaries = email
            .Attachments.Select(a => new AttachmentSummary(
                a.Id,
                a.Filename,
                a.ContentType,
                a.SizeBytes,
                a.IsInline,
                a.DownloadStatus.ToString(),
                a.CloudStorageFileId
            ))
            .ToList();

        return Result.Success(summaries);
    }
}
