using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Application.Rendering;

/// <summary>
/// Pedido de render de un evento. Carga <see cref="EventKey"/> (no un TemplateKey literal) porque
/// el primer paso del pipeline siempre es resolverlo vía EventTemplateResolver — el llamador (ej.
/// Postmaster en Fase 7) no conoce ni debe conocer qué template concreto responde a un evento.
/// LogoScope default System: un caller que no lo setea explícitamente (ej. tests previos a Fase
/// 4.5) sigue funcionando igual que antes de que existiera el logo pipeline.
/// </summary>
public sealed record RenderRequest(
    EventKey EventKey,
    Guid? TenantId,
    Locale? Locale,
    IReadOnlyDictionary<string, object?> Variables,
    LogoScope LogoScope = LogoScope.System
);

public sealed record RenderedContent(
    string Subject,
    string Html,
    string? Text,
    IReadOnlyList<InlineAsset> InlineAssets
);
