using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using BuildingBlocks.Results;
using Fluid;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Infrastructure.Templates;

/// <summary>
/// Renderer basado en Fluid (Liquid sandboxed). Se eligió Fluid en lugar de Scriban por su modelo
/// seguro para plantillas escritas por usuarios y por no arrastrar vulnerabilidades conocidas.
/// El cuerpo HTML se renderiza con auto-escape de los valores de variables (previene inyección HTML);
/// subject y cuerpo de texto sin escape. Solo se aceptan variables de la whitelist.
/// </summary>
public sealed partial class FluidTemplateRenderer : ITemplateRenderer
{
    private static readonly FluidParser Parser = new();

    public Result<RenderedContent> Render(RenderRequest request)
    {
        if (request.AllowedVariables.Count > 0)
        {
            var unknown = request
                .Variables.Keys.Where(k => !request.AllowedVariables.Contains(k, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (unknown.Count > 0)
                return Result.Failure<RenderedContent>(
                    new Error("EmailTemplate.Variable", $"Variables not allowed: {string.Join(", ", unknown)}.")
                );
        }

        var subject = RenderOne(request.SubjectTemplate ?? string.Empty, request.Variables, htmlEncode: false);
        if (subject.IsFailure)
            return Result.Failure<RenderedContent>(subject.Error);

        var html = RenderOne(request.HtmlBodyTemplate ?? string.Empty, request.Variables, htmlEncode: true);
        if (html.IsFailure)
            return Result.Failure<RenderedContent>(html.Error);

        var textSource = string.IsNullOrEmpty(request.TextBodyTemplate)
            ? HtmlToText(html.Value)
            : request.TextBodyTemplate!;
        var text = RenderOne(textSource, request.Variables, htmlEncode: false);
        if (text.IsFailure)
            return Result.Failure<RenderedContent>(text.Error);

        return Result.Success(new RenderedContent(subject.Value, html.Value, text.Value));
    }

    public Result<string> ApplyLayout(string layoutHtml, string renderedBodyHtml)
    {
        if (string.IsNullOrWhiteSpace(layoutHtml))
            return Result.Success(renderedBodyHtml);

        if (!BodyPlaceholder().IsMatch(layoutHtml))
            return Result.Failure<string>(
                new Error("EmailLayout.Body", "Layout HTML must contain a {{ body }} placeholder.")
            );

        return Result.Success(BodyPlaceholder().Replace(layoutHtml, renderedBodyHtml));
    }

    private static Result<string> RenderOne(string source, IReadOnlyDictionary<string, string?> vars, bool htmlEncode)
    {
        if (!Parser.TryParse(source, out var template, out var parseError))
            return Result.Failure<string>(new Error("EmailTemplate.Render", parseError));

        var context = new TemplateContext();
        foreach (var (key, value) in vars)
            context.SetValue(key, value ?? string.Empty);

        try
        {
            var rendered = htmlEncode ? template.Render(context, HtmlEncoder.Default) : template.Render(context);
            return Result.Success(rendered);
        }
        catch (Exception ex)
        {
            return Result.Failure<string>(new Error("EmailTemplate.Render", ex.Message));
        }
    }

    private static string HtmlToText(string html) =>
        WebUtility.HtmlDecode(WhitespaceRegex().Replace(TagRegex().Replace(html, " "), " ")).Trim();

    [GeneratedRegex(@"\{\{\s*body\s*\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex BodyPlaceholder();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
