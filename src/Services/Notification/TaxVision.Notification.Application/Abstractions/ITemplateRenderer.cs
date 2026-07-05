using BuildingBlocks.Results;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>Petición de renderizado: plantillas (Liquid) + variables + whitelist de variables permitidas.</summary>
public sealed record RenderRequest(
    string SubjectTemplate,
    string HtmlBodyTemplate,
    string? TextBodyTemplate,
    IReadOnlyDictionary<string, string?> Variables,
    IReadOnlyList<string> AllowedVariables
);

public sealed record RenderedContent(string Subject, string HtmlBody, string TextBody);

/// <summary>
/// Renderiza plantillas con variables. La implementación debe sanitizar los valores para evitar
/// inyección HTML (auto-escape en el cuerpo HTML) y validar contra la lista de variables permitidas.
/// </summary>
public interface ITemplateRenderer
{
    Result<RenderedContent> Render(RenderRequest request);

    /// <summary>Envuelve un cuerpo HTML ya renderizado dentro del HTML de un layout (marcador {{ body }}).</summary>
    Result<string> ApplyLayout(string layoutHtml, string renderedBodyHtml);
}
