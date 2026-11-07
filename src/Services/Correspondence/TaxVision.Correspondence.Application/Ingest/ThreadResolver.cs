using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Ingest;

/// <summary>
/// Resuelve a qué <see cref="EmailThread"/> pertenece un mensaje entrante, en 4 capas de
/// prioridad decreciente (plan de diseño §36 Fase 6). Capas 1-3 formalizan lo que
/// <see cref="RawMessageReceivedConsumer"/> traía inline desde Fase 4; la capa 4 es nueva:
///
/// <list type="number">
/// <item>ProviderThreadId — match exacto por <c>(TenantId, CustomerId, ProviderThreadId)</c>. Prioridad máxima.</item>
/// <item>InReplyTo — el <see cref="IncomingEmail"/> del mismo cliente dueño de ese <c>InternetMessageId</c> o, si no
/// hay, el enviado del CRM con ese Message-ID.</item>
/// <item>References — igual que InReplyTo pero sobre la lista completa; si hay más de un match
/// se queda con el de <see cref="IncomingEmail.ReceivedAtUtc"/> más reciente.</item>
/// <item>Subject fallback (opcional, <see cref="CorrespondenceIngestOptions.EnableSubjectThreadingFallback"/>,
/// default <c>false</c>) — busca un thread reciente del mismo customer cuyo subject normalizado
/// (<see cref="SubjectNormalizer"/>) coincida. Apagado por default a propósito: es una
/// heurística sin ninguna cabecera real de threading detrás, el plan la marca "opcional".</item>
/// </list>
///
/// Solo busca — nunca crea ni muta un <see cref="EmailThread"/>. Esa decisión (append vs. new)
/// queda en el caller, guiada por <see cref="EmailThreadResolution"/>.
/// </summary>
public sealed class ThreadResolver(
    IEmailThreadRepository threads,
    IIncomingEmailRepository incomingEmails,
    IDraftRepository drafts,
    IOptions<CorrespondenceIngestOptions> options
)
{
    /// <summary>
    /// Ventana de recencia para el fallback de subject. El plan la deja "opcional" sin un
    /// número concreto; 7 días es un default defendible para "probablemente la misma
    /// conversación sin cabeceras de threading" sin arriesgarse a mergear dos hilos distintos
    /// que por coincidencia comparten un subject genérico semanas después.
    /// </summary>
    private static readonly TimeSpan SubjectFallbackWindow = TimeSpan.FromDays(7);

    // COR-02: un hilo por cliente. Ninguna capa empareja con el hilo (o el correo) de otro cliente.
    public async Task<EmailThreadResolution> ResolveAsync(
        Guid tenantId,
        Guid? customerId,
        string? providerThreadId,
        string? inReplyTo,
        IReadOnlyList<string>? references,
        string subject,
        DateTime receivedAtUtc,
        CancellationToken ct
    )
    {
        if (providerThreadId is not null)
        {
            var byProviderThreadId = await threads.FindByProviderThreadIdAsync(
                tenantId,
                customerId,
                providerThreadId,
                ct
            );
            if (byProviderThreadId is not null)
                return new EmailThreadResolution(byProviderThreadId, ThreadMatchLayer.ProviderThreadId);
        }

        if (inReplyTo is not null)
        {
            var byInReplyTo = await ResolveByMessageIdsAsync(
                tenantId,
                customerId,
                [inReplyTo],
                ThreadMatchLayer.InReplyTo,
                ct
            );
            if (byInReplyTo is not null)
                return byInReplyTo;
        }

        if (references is { Count: > 0 })
        {
            var byReferences = await ResolveByMessageIdsAsync(
                tenantId,
                customerId,
                references,
                ThreadMatchLayer.References,
                ct
            );
            if (byReferences is not null)
                return byReferences;
        }

        // Sin cliente no hay "hilos del mismo cliente" contra los que comparar el asunto.
        if (!options.Value.EnableSubjectThreadingFallback || customerId is not { } owner)
            return EmailThreadResolution.NoMatch;

        var bySubject = await ResolveBySubjectFallbackAsync(tenantId, owner, subject, receivedAtUtc, ct);
        return bySubject is null
            ? EmailThreadResolution.NoMatch
            : new EmailThreadResolution(bySubject, ThreadMatchLayer.SubjectFallback);
    }

    /// <summary>
    /// Capas 2 y 3: primero los entrantes del mismo cliente (el más reciente gana) y, si no hay, los enviados desde
    /// el CRM (COR-16). Un enviado sin hilo vuelve en <see cref="EmailThreadResolution.UnthreadedSentDraft"/>.
    /// </summary>
    private async Task<EmailThreadResolution?> ResolveByMessageIdsAsync(
        Guid tenantId,
        Guid? customerId,
        IReadOnlyList<string> messageIds,
        ThreadMatchLayer layer,
        CancellationToken ct
    )
    {
        IncomingEmail? mostRecent = null;
        foreach (var messageId in messageIds)
        {
            var candidate = await incomingEmails.FindByInternetMessageIdAsync(tenantId, messageId, ct);
            if (
                candidate is not null
                && candidate.CustomerId == customerId
                && (mostRecent is null || candidate.ReceivedAtUtc > mostRecent.ReceivedAtUtc)
            )
                mostRecent = candidate;
        }

        if (mostRecent is not null)
        {
            var thread = await OwnThreadAsync(tenantId, customerId, mostRecent.EmailThreadId, ct);
            if (thread is not null)
                return new EmailThreadResolution(thread, layer);
        }

        var sent = await FindMostRecentSentAsync(tenantId, customerId, messageIds, ct);
        if (sent is null)
            return null;
        if (sent.EmailThreadId is not { } sentThreadId)
            return new EmailThreadResolution(null, layer, sent);

        var sentThread = await OwnThreadAsync(tenantId, customerId, sentThreadId, ct);
        return sentThread is null ? null : new EmailThreadResolution(sentThread, layer);
    }

    private async Task<Draft?> FindMostRecentSentAsync(
        Guid tenantId,
        Guid? customerId,
        IReadOnlyList<string> messageIds,
        CancellationToken ct
    )
    {
        Draft? mostRecent = null;
        foreach (var messageId in messageIds)
        {
            var normalized = Draft.NormalizeMessageId(messageId);
            if (normalized is null)
                continue;

            var candidate = await drafts.FindSentByInternetMessageIdAsync(tenantId, normalized, ct);
            if (
                candidate is not null
                && candidate.CustomerId == customerId
                && (mostRecent is null || candidate.UpdatedAtUtc > mostRecent.UpdatedAtUtc)
            )
                mostRecent = candidate;
        }

        return mostRecent;
    }

    private async Task<EmailThread?> OwnThreadAsync(
        Guid tenantId,
        Guid? customerId,
        Guid threadId,
        CancellationToken ct
    )
    {
        var thread = await threads.GetByIdAsync(tenantId, threadId, ct);
        return thread?.CustomerId == customerId ? thread : null;
    }

    private async Task<EmailThread?> ResolveBySubjectFallbackAsync(
        Guid tenantId,
        Guid customerId,
        string subject,
        DateTime receivedAtUtc,
        CancellationToken ct
    )
    {
        var normalizedSubject = SubjectNormalizer.Normalize(subject);
        var recentThreads = await threads.FindRecentByCustomerAsync(
            tenantId,
            customerId,
            receivedAtUtc - SubjectFallbackWindow,
            ct
        );
        return recentThreads.FirstOrDefault(t => SubjectNormalizer.Normalize(t.Subject) == normalizedSubject);
    }
}
