using BuildingBlocks.Results;

namespace TaxVision.Tenant.Domain.ValueObjects;

/// <summary>
/// Enlace público http(s) (sitio web, citas, reseñas). Sin esquema se asume <c>https://</c>. El tope y el
/// error dependen del campo: el sitio web viaja a columnas de 256 en las proyecciones.
/// </summary>
public sealed record WebUrl
{
    public string Value { get; }

    private WebUrl(string value) => Value = value;

    public static Result<WebUrl?> CreateOptional(string? raw, int maxLength, Error whenInvalid)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Result.Success<WebUrl?>(null);

        var candidate = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : $"https://{trimmed}";
        if (candidate.Length > maxLength || HasWhiteSpace(candidate))
            return Result.Failure<WebUrl?>(whenInvalid);

        if (
            !Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !IsPublicHost(uri.Host)
        )
            return Result.Failure<WebUrl?>(whenInvalid);

        return Result.Success<WebUrl?>(new(candidate));
    }

    public override string ToString() => Value;

    private static bool HasWhiteSpace(string value)
    {
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
                return true;
        }
        return false;
    }

    // Un dominio con punto y sin bordes vacíos ("yourfirm.com"); descarta "localhost" o "abc".
    private static bool IsPublicHost(string host) => host.Contains('.') && !host.StartsWith('.') && !host.EndsWith('.');
}
