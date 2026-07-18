using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Messages;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Threads;

/// <summary>
/// Listado paginado de los mensajes de UN hilo (Fase 9) — a diferencia de
/// <see cref="ListCustomerThreadsHandler"/>, este SÍ carga un recurso puntual primero (el hilo)
/// para poder devolver 404 si no existe o pertenece a otro tenant, antes de listar sus mensajes;
/// mismo criterio de "confirmar tenencia antes de listar hijos" que
/// <see cref="Messages.ListMessageAttachmentsHandler"/> usa sobre <c>IncomingEmail</c>.
///
/// <para>
/// Fase 15 — "thread unificado": fusiona los <c>IncomingEmail</c> (inbound) con los <c>Draft</c>
/// <see cref="DraftStatus.Sent"/> del mismo hilo (outbound, ver <see cref="Draft.EmailThreadId"/>)
/// en una sola lista cronológica ascendente (más viejo primero), y pagina recién sobre el
/// resultado YA mezclado — nunca por fuente por separado (ver <see cref="Paginate"/>). Ambas
/// fuentes se traen completas y sin paginar (<see cref="IIncomingEmailRepository.ListAllByThreadAsync"/>/
/// <see cref="IDraftRepository.ListSentByThreadAsync"/>): un hilo real está acotado
/// (<see cref="EmailThread.MessageCount"/> lo trackea), así que cargarlo entero en memoria antes
/// de paginar es seguro y evita el problema de "página 1 = 20 inbound + 20 outbound" que tendría
/// paginar cada fuente por separado.
/// </para>
/// </summary>
public static class ListThreadMessagesHandler
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public static async Task<Result<PagedResult<MessageSummary>>> Handle(
        ListThreadMessagesQuery query,
        IEmailThreadRepository emailThreads,
        IIncomingEmailRepository incomingEmails,
        IDraftRepository drafts,
        CancellationToken ct
    )
    {
        var thread = await emailThreads.GetByIdAsync(query.TenantId, query.ThreadId, ct);
        if (thread is null)
            return Result.Failure<PagedResult<MessageSummary>>(
                new Error("EmailThread.NotFound", "The thread was not found for this tenant.")
            );

        var inbound = await incomingEmails.ListAllByThreadAsync(query.TenantId, thread.Id, ct);
        var outbound = await drafts.ListSentByThreadAsync(query.TenantId, thread.Id, ct);

        var merged = MergeChronologically(inbound, outbound);
        return Result.Success(Paginate(merged, query.Page, query.Size));
    }

    private static List<MessageSummary> MergeChronologically(
        IReadOnlyList<IncomingEmail> inbound,
        IReadOnlyList<Draft> outbound
    )
    {
        var items = new List<MessageSummary>(inbound.Count + outbound.Count);
        items.AddRange(inbound.Select(GetMessageMetadataHandler.ToSummary));
        items.AddRange(outbound.Select(ToOutboundSummary));
        items.Sort((a, b) => a.OccurredAtUtc.CompareTo(b.OccurredAtUtc));
        return items;
    }

    /// <summary>
    /// Paginación sobre la lista YA fusionada — replica el mismo clamping (default 20, máx 100)
    /// que antes vivía en <c>IncomingEmailRepository.ListByThreadAsync</c>, ahora relocado acá
    /// porque la fuente de datos ya no es una sola query SQL paginable, sino dos listas completas
    /// mezcladas en memoria.
    /// </summary>
    private static PagedResult<MessageSummary> Paginate(List<MessageSummary> merged, int page, int size)
    {
        var normalizedPage = page < 1 ? 1 : page;
        var normalizedSize = size switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => size,
        };

        var items = merged.Skip((normalizedPage - 1) * normalizedSize).Take(normalizedSize).ToList();
        return new PagedResult<MessageSummary>(items, normalizedPage, normalizedSize, merged.Count);
    }

    private static MessageSummary ToOutboundSummary(Draft draft) =>
        new(
            draft.Id,
            MessageDirection.Outbound,
            From: null,
            FromDisplayName: null,
            draft.Subject,
            Snippet: null,
            ToAddresses: draft.Recipients.Where(r => r.Type == EmailRecipientType.To).Select(r => r.Address).ToList(),
            // Ver el WHY-comment de Draft.MarkSent: UpdatedAtUtc ES el instante de envío, ningún
            // otro método del aggregate lo vuelve a tocar una vez Sent.
            OccurredAtUtc: draft.UpdatedAtUtc,
            HasAttachments: draft.Attachments.Count > 0,
            AttachmentCount: draft.Attachments.Count,
            BodyStatus: null
        );
}
