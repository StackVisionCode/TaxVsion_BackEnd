using BuildingBlocks.Results;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Application.Rendering;

/// <summary>
/// Entry point único de Application para "renderizar un email por EventKey" — Fase 7. El HTTP
/// (POST /scribe/render) invoca este query via IMessageBus, igual que el resto de los endpoints
/// del servicio (nunca llaman IEmailRenderer directo desde el controller). Un gRPC equivalente
/// existió en paralelo hasta la Fase 8 del hardening, retirado por falta de caller real
/// (ver ADR-0003).
/// </summary>
public sealed record RenderEmailQuery(
    string EventKey,
    Guid? TenantId,
    string? Locale,
    IReadOnlyDictionary<string, object?> Variables,
    LogoScope LogoScope = LogoScope.System
);

public static class RenderEmailHandler
{
    public static async Task<Result<RenderedContent>> Handle(
        RenderEmailQuery query,
        IEmailRenderer renderer,
        CancellationToken ct
    )
    {
        var eventKeyResult = EventKey.Create(query.EventKey);
        if (eventKeyResult.IsFailure)
            return Result.Failure<RenderedContent>(eventKeyResult.Error);

        Locale? locale = null;
        if (!string.IsNullOrWhiteSpace(query.Locale))
        {
            var localeResult = Locale.Create(query.Locale);
            if (localeResult.IsFailure)
                return Result.Failure<RenderedContent>(localeResult.Error);
            locale = localeResult.Value;
        }

        var request = new RenderRequest(eventKeyResult.Value, query.TenantId, locale, query.Variables, query.LogoScope);
        return await renderer.RenderAsync(request, ct);
    }
}
