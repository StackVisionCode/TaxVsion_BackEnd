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

/// <param name="DispatchScope">
/// De quien sale el correo, decidido por el LAYOUT: <c>tenant-base</c> es la cascara de la oficina, asi
/// que sale por su buzon. Viaja como string, no como enum: el enum real vive en Notification y una
/// diferencia de serializacion aca se pierde en silencio, que es como se perdio InlineAssets entero.
/// </param>
public sealed record RenderedContent(
    string Subject,
    string Html,
    string? Text,
    IReadOnlyList<InlineAsset> InlineAssets,
    string DispatchScope
)
{
    /// <summary>Preserva a los callers y tests anteriores al carril por layout.</summary>
    public RenderedContent(string Subject, string Html, string? Text, IReadOnlyList<InlineAsset> InlineAssets)
        : this(Subject, Html, Text, InlineAssets, DispatchScopes.System) { }
}

/// <summary>Los dos carriles que el layout puede pedir. Strings, porque cruzan HTTP.</summary>
public static class DispatchScopes
{
    public const string System = "System";
    public const string TenantPreferred = "TenantPreferred";
}
