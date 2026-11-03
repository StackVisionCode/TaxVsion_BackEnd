using System.Globalization;
using System.Text.RegularExpressions;

namespace TaxVision.Postmaster.Domain.ValueObjects;

/// <summary>
/// Código de estado extendido RFC 3463 (<c>clase.asunto.detalle</c>, ej. 5.1.1): clase 2 éxito,
/// 4 temporal, 5 permanente. Es protocolo, no vocabulario de un proveedor.
/// </summary>
public sealed partial record EnhancedStatusCode
{
    public int Class { get; }
    public int Subject { get; }
    public int Detail { get; }

    private EnhancedStatusCode(int @class, int subject, int detail)
    {
        Class = @class;
        Subject = subject;
        Detail = detail;
    }

    public bool IsPermanent => Class == 5;

    /// <summary>
    /// Primer código que aparezca en el texto: el campo Status de un DSN ("5.1.1") o una respuesta SMTP
    /// ("550 5.1.1 user unknown"). No toma pedazos de una IP ni de una versión. Null si no hay.
    /// </summary>
    public static EnhancedStatusCode? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = CodePattern().Match(text);
        if (!match.Success)
            return null;

        return new EnhancedStatusCode(
            int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)
        );
    }

    public override string ToString() => $"{Class}.{Subject}.{Detail}";

    [GeneratedRegex(@"(?<![\d.])([245])\.(\d{1,3})\.(\d{1,3})(?![\d.])", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
