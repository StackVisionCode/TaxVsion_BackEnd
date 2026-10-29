using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Html;
using AngleSharp.Html.Parser;
using AngleSharp.Html.Parser.Tokens;
using AngleSharp.Text;

namespace TaxVision.Scribe.Application.Templates.Validation;

/// <summary>
/// Reglas de contenido activo (XSS) del preflight de email, sobre los tokens del tokenizer HTML5 de
/// AngleSharp (entidades, mayúsculas y comillas ya resueltas como en un navegador). Se usa el
/// tokenizer y no el árbol porque una plantilla es un fragmento que se incrusta en un layout: el árbol
/// descarta etiquetas fuera de contexto (un <c>&lt;td onclick&gt;</c> suelto) que dentro del layout sí
/// quedan vivas. Todo se tokeniza como markup, también dentro de <c>&lt;style&gt;</c>/<c>&lt;svg&gt;</c>,
/// para no perder evasiones mXSS. Revisa además el contenido de los comentarios condicionales de
/// Outlook (<c>&lt;!--[if mso]&gt;…&lt;![endif]--&gt;</c>). Los placeholders Liquid quedan como texto o
/// valor de atributo y no disparan reglas. Un error por código, como el resto del validador.
/// </summary>
internal static partial class EmailHtmlActiveContentInspector
{
    private const int MaxConditionalCommentDepth = 3;
    private const string CssScriptMessage =
        "CSS must not use expression(), behavior, -moz-binding or javascript:/vbscript: URLs.";

    private static readonly HashSet<string> UrlAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href",
        "xlink:href",
        "src",
        "srcset",
        "action",
        "formaction",
        "background",
        "poster",
        "data",
        "codebase",
        "archive",
        "cite",
        "longdesc",
        "lowsrc",
        "dynsrc",
        "icon",
        "manifest",
        "ping",
    };

    private static readonly string[] SafeDataImagePrefixes =
    [
        "data:image/png",
        "data:image/jpeg",
        "data:image/jpg",
        "data:image/gif",
        "data:image/webp",
    ];

    public static void Inspect(string html, List<EmailHtmlSafetyIssue> errors)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);
        void Report(string code, string message)
        {
            if (reported.Add(code))
                errors.Add(new EmailHtmlSafetyIssue(code, message));
        }

        InspectSource(html, Report, depth: 0);
    }

    private static void InspectSource(string html, Action<string, string> report, int depth)
    {
        var tokenizer = new HtmlTokenizer(new TextSource(html), HtmlEntityProvider.Resolver);
        StringBuilder? styleText = null;

        while (true)
        {
            var token = tokenizer.Get();
            switch (token.Type)
            {
                case HtmlTokenType.EndOfFile:
                    if (styleText is not null && IsScriptableCss(styleText.ToString()))
                        report("EmailHtmlSafety.CssScript", CssScriptMessage);
                    return;
                case HtmlTokenType.StartTag:
                    var tag = token.AsTag();
                    InspectTag(tag, report);
                    if (tag.Name == "style")
                        styleText = new StringBuilder();
                    break;
                case HtmlTokenType.EndTag when token.Name == "style" && styleText is not null:
                    if (IsScriptableCss(styleText.ToString()))
                        report("EmailHtmlSafety.CssScript", CssScriptMessage);
                    styleText = null;
                    break;
                case HtmlTokenType.Character when styleText is not null:
                    styleText.Append(token.Data);
                    break;
                case HtmlTokenType.Comment when depth < MaxConditionalCommentDepth:
                    if (TryGetConditionalCommentBody(token.Data, out var body))
                        InspectSource(body, report, depth + 1);
                    break;
            }
        }
    }

    private static void InspectTag(HtmlTagToken tag, Action<string, string> report)
    {
        switch (tag.Name.ToLowerInvariant())
        {
            case "script":
                report("EmailHtmlSafety.Script", "<script> is stripped by email clients and may trigger spam filters.");
                break;
            case "iframe" or "frame" or "frameset":
                report("EmailHtmlSafety.Iframe", "<iframe> is not supported by email clients.");
                break;
            case "object" or "applet":
                report("EmailHtmlSafety.Object", "<object> is not allowed in email templates.");
                break;
            case "embed":
                report("EmailHtmlSafety.Embed", "<embed> is not allowed in email templates.");
                break;
            case "form":
                report("EmailHtmlSafety.Form", "<form> is not allowed in email templates.");
                break;
            case "meta" when tag.Attributes.Any(a => IsName(a, "http-equiv") && IsRefresh(a.Value)):
                report(
                    "EmailHtmlSafety.MetaRefresh",
                    "<meta http-equiv=\"refresh\"> is not allowed in email templates."
                );
                break;
        }

        foreach (var attribute in tag.Attributes)
            InspectAttribute(attribute, report);
    }

    private static void InspectAttribute(HtmlAttributeToken attribute, Action<string, string> report)
    {
        var name = attribute.Name;
        if (name.Length > 2 && name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            report(
                "EmailHtmlSafety.EventHandler",
                $"Event handler attributes (on*=) are not allowed in email templates (found '{name}')."
            );

        var value = NormalizeUrl(attribute.Value);
        var isScriptUrl =
            value.StartsWith("javascript:", StringComparison.Ordinal)
            || value.StartsWith("vbscript:", StringComparison.Ordinal);
        if (isScriptUrl || (UrlAttributes.Contains(name) && IsUnsafeDataUrl(value)))
            report(
                "EmailHtmlSafety.ScriptUrl",
                $"javascript:, vbscript: and data: URLs are not allowed in email templates (found in '{name}')."
            );

        if (IsName(attribute, "style") && IsScriptableCss(attribute.Value))
            report("EmailHtmlSafety.CssScript", CssScriptMessage);
    }

    private static bool IsName(HtmlAttributeToken attribute, string name) =>
        string.Equals(attribute.Name, name, StringComparison.OrdinalIgnoreCase);

    // Solo imágenes raster como data:; data:text/html, data:image/svg+xml y demás se rechazan.
    private static bool IsUnsafeDataUrl(string normalizedValue) =>
        normalizedValue.StartsWith("data:", StringComparison.Ordinal)
        && !SafeDataImagePrefixes.Any(prefix => normalizedValue.StartsWith(prefix, StringComparison.Ordinal));

    private static bool IsRefresh(string? httpEquiv) =>
        string.Equals(httpEquiv?.Trim(), "refresh", StringComparison.OrdinalIgnoreCase);

    private static bool IsScriptableCss(string css)
    {
        var compact = NormalizeUrl(CssCommentPattern().Replace(css, string.Empty)).Replace("\\", string.Empty);
        return compact.Contains("expression(", StringComparison.Ordinal)
            || compact.Contains("javascript:", StringComparison.Ordinal)
            || compact.Contains("vbscript:", StringComparison.Ordinal)
            || compact.Contains("behavior:", StringComparison.Ordinal)
            || compact.Contains("-moz-binding", StringComparison.Ordinal);
    }

    // El navegador ignora espacios/controles dentro del esquema ("java\tscript:"); se quitan todos.
    private static string NormalizeUrl(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
            if (c > ' ')
                builder.Append(char.ToLowerInvariant(c));
        return builder.ToString();
    }

    // "[if mso]>…<![endif]" → el HTML de adentro; los comentarios normales son inertes.
    private static bool TryGetConditionalCommentBody(string data, out string body)
    {
        body = string.Empty;
        var trimmed = data.TrimStart();
        if (!trimmed.StartsWith("[if", StringComparison.OrdinalIgnoreCase))
            return false;

        var start = trimmed.IndexOf('>');
        if (start < 0)
            return false;

        var end = trimmed.LastIndexOf("<![endif]", StringComparison.OrdinalIgnoreCase);
        body = end > start ? trimmed[(start + 1)..end] : trimmed[(start + 1)..];
        return true;
    }

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CssCommentPattern();
}
