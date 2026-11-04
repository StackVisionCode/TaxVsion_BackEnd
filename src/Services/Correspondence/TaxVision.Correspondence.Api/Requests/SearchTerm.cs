namespace TaxVision.Correspondence.Api.Requests;

/// <summary>Normaliza el <c>q</c> de los listados y de <c>/search</c>: recortado, vacío = sin filtro, tope de largo.</summary>
internal static class SearchTerm
{
    public const int MaxLength = 200;

    /// <summary>Mínimo para <c>/correspondence/search</c> (sugerencias): con una letra devolvería medio buzón.</summary>
    public const int MinLength = 2;

    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var trimmed = raw.Trim();
        return trimmed.Length <= MaxLength ? trimmed : trimmed[..MaxLength];
    }
}
