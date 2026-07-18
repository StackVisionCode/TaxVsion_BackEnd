using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Ingest;

/// <summary>
/// Resuelve a qué <see cref="EmailThread"/> pertenece un mensaje entrante, en 4 capas de
/// prioridad decreciente (plan de diseño §36 Fase 6). Capas 1-3 formalizan lo que
/// <see cref="RawMessageReceivedConsumer"/> traía inline desde Fase 4; la capa 4 es nueva:
///
/// <list type="number">
/// <item>ProviderThreadId — match exacto por <c>(TenantId, ProviderThreadId)</c>. Prioridad máxima.</item>
/// <item>InReplyTo — el <see cref="IncomingEmail"/> dueño de ese <c>InternetMessageId</c>, si existe.</item>
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

    public async Task<EmailThreadResolution> ResolveAsync(
        Guid tenantId,
        Guid customerId,
        string? providerThreadId,
        string? inReplyTo,
        IReadOnlyList<string>? references,
        string subject,
        DateTime receivedAtUtc,
        CancellationToken ct
    )
    {
        var byProviderThreadId = await ResolveByProviderThreadIdAsync(tenantId, providerThreadId, ct);
        if (byProviderThreadId is not null)
            return new EmailThreadResolution(byProviderThreadId, ThreadMatchLayer.ProviderThreadId);

        var byInReplyTo = await ResolveByInReplyToAsync(tenantId, inReplyTo, ct);
        if (byInReplyTo is not null)
            return new EmailThreadResolution(byInReplyTo, ThreadMatchLayer.InReplyTo);

        var byReferences = await ResolveByReferencesAsync(tenantId, references, ct);
        if (byReferences is not null)
            return new EmailThreadResolution(byReferences, ThreadMatchLayer.References);

        if (!options.Value.EnableSubjectThreadingFallback)
            return EmailThreadResolution.NoMatch;

        var bySubject = await ResolveBySubjectFallbackAsync(tenantId, customerId, subject, receivedAtUtc, ct);
        return bySubject is null
            ? EmailThreadResolution.NoMatch
            : new EmailThreadResolution(bySubject, ThreadMatchLayer.SubjectFallback);
    }

    private Task<EmailThread?> ResolveByProviderThreadIdAsync(
        Guid tenantId,
        string? providerThreadId,
        CancellationToken ct
    ) =>
        providerThreadId is null
            ? Task.FromResult<EmailThread?>(null)
            : threads.FindByProviderThreadIdAsync(tenantId, providerThreadId, ct);

    private async Task<EmailThread?> ResolveByInReplyToAsync(Guid tenantId, string? inReplyTo, CancellationToken ct)
    {
        if (inReplyTo is null)
            return null;

        var related = await incomingEmails.FindByInternetMessageIdAsync(tenantId, inReplyTo, ct);
        return related is null ? null : await threads.GetByIdAsync(tenantId, related.EmailThreadId, ct);
    }

    private async Task<EmailThread?> ResolveByReferencesAsync(
        Guid tenantId,
        IReadOnlyList<string>? references,
        CancellationToken ct
    )
    {
        if (references is not { Count: > 0 })
            return null;

        var mostRecent = await FindMostRecentReferencedEmailAsync(tenantId, references, ct);
        return mostRecent is null ? null : await threads.GetByIdAsync(tenantId, mostRecent.EmailThreadId, ct);
    }

    private async Task<IncomingEmail?> FindMostRecentReferencedEmailAsync(
        Guid tenantId,
        IReadOnlyList<string> references,
        CancellationToken ct
    )
    {
        IncomingEmail? mostRecent = null;
        foreach (var reference in references)
        {
            var candidate = await incomingEmails.FindByInternetMessageIdAsync(tenantId, reference, ct);
            if (candidate is not null && (mostRecent is null || candidate.ReceivedAtUtc > mostRecent.ReceivedAtUtc))
                mostRecent = candidate;
        }

        return mostRecent;
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
