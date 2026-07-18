using System.Text.RegularExpressions;

namespace TaxVision.Scribe.Application.Templates.Validation;

/// <summary>Regla concreta incumplida (para el log de auditoría de la version y, más adelante, el frontend de admin).</summary>
public sealed record EmailHtmlSafetyIssue(string Code, string Message);

/// <summary>
/// Resultado del preflight: <see cref="Errors"/> no vacío bloquea el publish; <see cref="Warnings"/>
/// no bloquea, solo se registra.
/// </summary>
public sealed record EmailHtmlSafetyValidationOutcome(
    bool IsAcceptable,
    IReadOnlyList<EmailHtmlSafetyIssue> Errors,
    IReadOnlyList<EmailHtmlSafetyIssue> Warnings
);

/// <summary>
/// Preflight de seguridad/compatibilidad de HTML de email (Fase 4.6, §Scribe_Email_Style_Guide.md).
/// Puro: solo regex sobre el string, sin dependencias externas. El wiring real en
/// PublishTemplateVersionHandler/PublishLayoutVersionHandler queda para Fase 5 — esos handlers
/// todavía no existen (Fase 2 los excluyó explícitamente).
/// </summary>
public sealed partial class EmailHtmlSafetyValidator
{
    public EmailHtmlSafetyValidationOutcome Validate(string html)
    {
        var errors = new List<EmailHtmlSafetyIssue>();
        var warnings = new List<EmailHtmlSafetyIssue>();

        CheckFlexbox(html, errors);
        CheckGrid(html, errors);
        CheckPositionAbsolute(html, errors);
        CheckPositionFixed(html, errors);
        CheckExternalStylesheet(html, errors);
        CheckScript(html, errors);
        CheckIframe(html, errors);
        CheckRemoteImages(html, errors);
        CheckImageDimensions(html, warnings);

        return new EmailHtmlSafetyValidationOutcome(errors.Count == 0, errors, warnings);
    }

    private static void CheckFlexbox(string html, List<EmailHtmlSafetyIssue> errors)
    {
        if (DisplayFlexPattern().IsMatch(html))
            errors.Add(
                new EmailHtmlSafetyIssue("EmailHtmlSafety.Flexbox", "display: flex is not supported by Outlook/Gmail.")
            );
    }

    private static void CheckGrid(string html, List<EmailHtmlSafetyIssue> errors)
    {
        if (DisplayGridPattern().IsMatch(html))
            errors.Add(
                new EmailHtmlSafetyIssue("EmailHtmlSafety.Grid", "display: grid is not supported by Outlook/Gmail.")
            );
    }

    private static void CheckPositionAbsolute(string html, List<EmailHtmlSafetyIssue> errors)
    {
        if (PositionAbsolutePattern().IsMatch(html))
            errors.Add(
                new EmailHtmlSafetyIssue(
                    "EmailHtmlSafety.PositionAbsolute",
                    "position: absolute is not reliably supported by email clients."
                )
            );
    }

    private static void CheckPositionFixed(string html, List<EmailHtmlSafetyIssue> errors)
    {
        if (PositionFixedPattern().IsMatch(html))
            errors.Add(
                new EmailHtmlSafetyIssue(
                    "EmailHtmlSafety.PositionFixed",
                    "position: fixed is not reliably supported by email clients."
                )
            );
    }

    private static void CheckExternalStylesheet(string html, List<EmailHtmlSafetyIssue> errors)
    {
        if (ExternalStylesheetPattern().IsMatch(html))
            errors.Add(
                new EmailHtmlSafetyIssue(
                    "EmailHtmlSafety.ExternalStylesheet",
                    "External stylesheets (<link rel=\"stylesheet\">) are stripped by most email clients."
                )
            );
    }

    private static void CheckScript(string html, List<EmailHtmlSafetyIssue> errors)
    {
        if (ScriptTagPattern().IsMatch(html))
            errors.Add(
                new EmailHtmlSafetyIssue(
                    "EmailHtmlSafety.Script",
                    "<script> is stripped by email clients and may trigger spam filters."
                )
            );
    }

    private static void CheckIframe(string html, List<EmailHtmlSafetyIssue> errors)
    {
        if (IframeTagPattern().IsMatch(html))
            errors.Add(
                new EmailHtmlSafetyIssue("EmailHtmlSafety.Iframe", "<iframe> is not supported by email clients.")
            );
    }

    private static void CheckRemoteImages(string html, List<EmailHtmlSafetyIssue> errors)
    {
        if (RemoteImagePattern().IsMatch(html))
            errors.Add(
                new EmailHtmlSafetyIssue(
                    "EmailHtmlSafety.RemoteImage",
                    "Images must be referenced via cid: (inline attachment), not http(s):// — Outlook classic blocks remote images by default."
                )
            );
    }

    private static void CheckImageDimensions(string html, List<EmailHtmlSafetyIssue> warnings)
    {
        foreach (Match imgTag in ImgTagPattern().Matches(html))
        {
            if (WidthAttributePattern().IsMatch(imgTag.Value) && HeightAttributePattern().IsMatch(imgTag.Value))
                continue;

            warnings.Add(
                new EmailHtmlSafetyIssue(
                    "EmailHtmlSafety.ImageMissingDimensions",
                    "One or more <img> tags are missing width/height attributes; some clients reflow layout before the image loads."
                )
            );
            return;
        }
    }

    [GeneratedRegex(@"display\s*:\s*flex", RegexOptions.IgnoreCase)]
    private static partial Regex DisplayFlexPattern();

    [GeneratedRegex(@"display\s*:\s*grid", RegexOptions.IgnoreCase)]
    private static partial Regex DisplayGridPattern();

    [GeneratedRegex(@"position\s*:\s*absolute", RegexOptions.IgnoreCase)]
    private static partial Regex PositionAbsolutePattern();

    [GeneratedRegex(@"position\s*:\s*fixed", RegexOptions.IgnoreCase)]
    private static partial Regex PositionFixedPattern();

    [GeneratedRegex(@"<link[^>]+rel\s*=\s*[""']stylesheet[""']", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalStylesheetPattern();

    [GeneratedRegex(@"<script\b", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTagPattern();

    [GeneratedRegex(@"<iframe\b", RegexOptions.IgnoreCase)]
    private static partial Regex IframeTagPattern();

    [GeneratedRegex(@"<img\b[^>]*\bsrc\s*=\s*[""']https?://", RegexOptions.IgnoreCase)]
    private static partial Regex RemoteImagePattern();

    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImgTagPattern();

    [GeneratedRegex(@"\bwidth\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex WidthAttributePattern();

    [GeneratedRegex(@"\bheight\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex HeightAttributePattern();
}
