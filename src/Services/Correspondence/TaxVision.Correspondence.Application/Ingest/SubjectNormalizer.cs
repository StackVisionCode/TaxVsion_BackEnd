using System.Text.RegularExpressions;

namespace TaxVision.Correspondence.Application.Ingest;

/// <summary>
/// Normaliza un subject de email para el fallback opcional de threading (Layer 4 de
/// <see cref="ThreadResolver"/>): quita prefijos de respuesta/reenvío (<c>Re:</c>, <c>RE:</c>,
/// <c>Fwd:</c>, <c>FW:</c>), posiblemente repetidos (p.ej. <c>"Re: Re: Fwd: Hello"</c>), y
/// colapsa espacios. Vive en su propio archivo, separado de <see cref="ThreadResolver"/>, para
/// poder testear la normalización en aislamiento sin repositorios de por medio.
/// </summary>
public static class SubjectNormalizer
{
    private static readonly Regex ReplyForwardPrefix = new(
        @"^(re|fwd|fw)\s*:\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );
    private static readonly Regex ExtraWhitespace = new(@"\s+", RegexOptions.Compiled);

    public static string Normalize(string subject)
    {
        var value = subject.Trim();

        string beforeStrip;
        do
        {
            beforeStrip = value;
            value = ReplyForwardPrefix.Replace(value, string.Empty).TrimStart();
        } while (!string.Equals(value, beforeStrip, StringComparison.Ordinal));

        return ExtraWhitespace.Replace(value, " ").Trim().ToLowerInvariant();
    }
}
