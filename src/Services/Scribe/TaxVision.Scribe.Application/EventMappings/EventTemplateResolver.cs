using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.EventMappings;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Application.EventMappings;

/// <summary>
/// Resuelve qué TemplateKey renderizar para un evento, con prioridad
/// Tenant+Locale &gt; Tenant sin Locale &gt; System+Locale &gt; System sin Locale &gt; null.
/// El índice único de EventTemplateMapping garantiza a lo sumo un candidato por tier; Priority solo
/// desempata si igual llegaran a coexistir varios (defensivo, no depende de eso para funcionar).
/// </summary>
public sealed class EventTemplateResolver(IEventTemplateMappingRepository repository)
{
    public async Task<TemplateKey?> ResolveAsync(
        EventKey eventKey,
        Guid? tenantId,
        Locale? locale,
        CancellationToken ct = default
    )
    {
        var candidates = await repository.GetEnabledForEventAsync(eventKey, tenantId, ct);
        if (candidates.Count == 0)
            return null;

        return SelectBest(candidates, TemplateScope.Tenant, locale)?.TemplateKey
            ?? SelectBest(candidates, TemplateScope.Tenant, requestedLocale: null)?.TemplateKey
            ?? SelectBest(candidates, TemplateScope.System, locale)?.TemplateKey
            ?? SelectBest(candidates, TemplateScope.System, requestedLocale: null)?.TemplateKey;
    }

    private static EventTemplateMapping? SelectBest(
        IReadOnlyList<EventTemplateMapping> candidates,
        TemplateScope scope,
        Locale? requestedLocale
    )
    {
        EventTemplateMapping? best = null;
        foreach (var candidate in candidates)
        {
            if (candidate.Scope != scope)
                continue;

            var localeMatches = requestedLocale is null
                ? candidate.Locale is null
                : candidate.Locale is not null && candidate.Locale.Value == requestedLocale.Value;
            if (!localeMatches)
                continue;

            if (best is null || candidate.Priority > best.Priority)
                best = candidate;
        }
        return best;
    }
}
