using AngleSharp.Css.Dom;
using AngleSharp.Dom;
using TaxVision.Notes.Application.Notes.Abstractions;
using GanssOptions = Ganss.Xss.HtmlSanitizerOptions;
using GanssSanitizer = Ganss.Xss.HtmlSanitizer;
using PostProcessNodeEventArgs = Ganss.Xss.PostProcessNodeEventArgs;

namespace TaxVision.Notes.Infrastructure.Notes;

/// <summary>
/// Implementación real de <see cref="IHtmlSanitizer"/> vía <c>Ganss.Xss.HtmlSanitizer</c> con allowlist
/// propia. El CRM hoy solo emite texto escapado con <c>&lt;br&gt;</c> (perfil de cliente) o
/// <c>&lt;p&gt;</c> (dashboard); la allowlist cubre además formato básico de texto enriquecido (negrita,
/// cursiva, subrayado, listas, encabezados, citas, código y enlaces) sin <c>style</c>, <c>class</c> ni
/// <c>data-*</c>. Enlaces solo http/https/mailto; <c>target</c> solo <c>_blank</c> y siempre con
/// <c>rel="noopener noreferrer"</c>. Instancia única: Ganss es thread-safe una vez configurado. Alias porque
/// <c>Ganss.Xss</c> define su propio <c>IHtmlSanitizer</c>, ambiguo con el nuestro.
/// </summary>
public sealed class GanssHtmlSanitizer : IHtmlSanitizer
{
    private static readonly string[] AllowedTags =
    [
        "p",
        "br",
        "div",
        "span",
        "b",
        "strong",
        "i",
        "em",
        "u",
        "s",
        "strike",
        "del",
        "ins",
        "mark",
        "sub",
        "sup",
        "small",
        "ul",
        "ol",
        "li",
        "blockquote",
        "pre",
        "code",
        "hr",
        "h1",
        "h2",
        "h3",
        "h4",
        "h5",
        "h6",
        "a",
    ];

    private static readonly string[] AllowedAttributes = ["href", "title", "target", "rel"];

    private static readonly string[] AllowedSchemes = ["http", "https", "mailto"];

    private readonly GanssSanitizer _sanitizer = Build();

    public string Sanitize(string rawHtml) => _sanitizer.Sanitize(rawHtml ?? string.Empty);

    private static GanssSanitizer Build()
    {
        var sanitizer = new GanssSanitizer(
            new GanssOptions
            {
                AllowedTags = new HashSet<string>(AllowedTags, StringComparer.OrdinalIgnoreCase),
                AllowedAttributes = new HashSet<string>(AllowedAttributes, StringComparer.OrdinalIgnoreCase),
                AllowedSchemes = new HashSet<string>(AllowedSchemes, StringComparer.OrdinalIgnoreCase),
                UriAttributes = new HashSet<string>(["href"], StringComparer.OrdinalIgnoreCase),
                AllowedCssProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                AllowedAtRules = new HashSet<CssRuleType>(),
                AllowedCssClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                AllowDataAttributes = false,
            }
        );
        sanitizer.PostProcessNode += EnforceSafeLinkTarget;
        return sanitizer;
    }

    // target distinto de _blank se descarta; _blank siempre lleva rel="noopener noreferrer".
    private static void EnforceSafeLinkTarget(object? sender, PostProcessNodeEventArgs e)
    {
        if (e.Node is not IElement element || element.LocalName != "a")
            return;

        var target = element.GetAttribute("target");
        if (target is null)
            return;

        if (!string.Equals(target.Trim(), "_blank", StringComparison.OrdinalIgnoreCase))
        {
            element.RemoveAttribute("target");
            return;
        }

        element.SetAttribute("target", "_blank");
        element.SetAttribute("rel", "noopener noreferrer");
    }
}
