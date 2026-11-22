using BuildingBlocks.Results;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Render sabiendo quién lee (Fase 5): Scribe imprime las fechas tipadas (<see cref="Common.ScribeDates"/>) en su
/// idioma y zona. Va aparte de <see cref="IScribeRenderClient"/> para no tocar sus dobles de prueba.
/// </summary>
public interface IScribeReaderRenderClient
{
    Task<Result<ScribeRenderedEmail>> RenderForReaderAsync(
        string eventKey,
        Guid tenantId,
        IReadOnlyDictionary<string, object?> variables,
        ScribeRenderAudience audience,
        ScribeReader reader,
        CancellationToken ct = default
    );
}

public static class ScribeRenderClientExtensions
{
    /// <summary>Con un cliente que sabe de lectores, manda idioma y zona; si no, el render de siempre.</summary>
    public static Task<Result<ScribeRenderedEmail>> RenderAsync(
        this IScribeRenderClient client,
        string eventKey,
        Guid tenantId,
        IReadOnlyDictionary<string, object?> variables,
        ScribeRenderAudience audience,
        ScribeReader reader,
        CancellationToken ct = default
    ) =>
        client is IScribeReaderRenderClient readerAware
            ? readerAware.RenderForReaderAsync(eventKey, tenantId, variables, audience, reader, ct)
            : client.RenderAsync(eventKey, tenantId, variables, audience, ct);

    /// <summary>
    /// Render en el idioma de quien lee: las dos señales de Scribe, la variable <c>language</c> (ramas de texto) y
    /// el lector (fechas y zona).
    /// </summary>
    public static Task<Result<ScribeRenderedEmail>> RenderForRecipientAsync(
        this IScribeRenderClient client,
        string eventKey,
        Guid tenantId,
        IReadOnlyDictionary<string, object?> variables,
        ScribeRenderAudience audience,
        RecipientLocale locale,
        CancellationToken ct = default
    ) =>
        client.RenderAsync(
            eventKey,
            tenantId,
            new Dictionary<string, object?>(variables) { ["language"] = locale.TemplateLanguage },
            audience,
            locale.Reader,
            ct
        );
}
