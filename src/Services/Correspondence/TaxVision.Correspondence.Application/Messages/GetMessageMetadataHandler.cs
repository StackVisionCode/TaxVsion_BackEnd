using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Messages;

/// <summary>
/// Vista de metadata de UN mensaje (Fase 9) — HTTP-triggered, no un consumer Wolverine, mismo
/// criterio que <see cref="GetMessageBodyHandler"/>/<see cref="ListMessageAttachmentsHandler"/>
/// (no empuja correlación). A diferencia de <see cref="GetMessageBodyHandler"/>, esto NUNCA
/// llama a Connectors: es puro metadata ya persistida en <see cref="IncomingEmail"/>, mismo DTO
/// (<see cref="MessageSummary"/>) que devuelve el listado paginado — no hay ningún campo extra
/// que la vista de un solo mensaje necesite por encima del listado.
/// </summary>
public static class GetMessageMetadataHandler
{
    public static async Task<Result<MessageSummary>> Handle(
        GetMessageMetadataQuery query,
        IIncomingEmailRepository incomingEmails,
        CancellationToken ct
    )
    {
        var email = await incomingEmails.GetByIdAsync(query.TenantId, query.IncomingEmailId, ct);
        return email is null
            ? Result.Failure<MessageSummary>(
                new Error("IncomingEmail.NotFound", "The message was not found for this tenant.")
            )
            : Result.Success(ToSummary(email));
    }

    internal static MessageSummary ToSummary(IncomingEmail email) =>
        new(
            email.Id,
            MessageDirection.Inbound,
            email.From,
            email.FromDisplayName,
            email.Subject,
            email.Snippet,
            ToAddresses: null,
            email.ReceivedAtUtc,
            email.HasAttachments,
            email.AttachmentCount,
            email.BodyStatus.ToString()
        );
}
