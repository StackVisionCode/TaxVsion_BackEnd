using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Search;

namespace TaxVision.Correspondence.Api.Requests;

/// <summary>Validación de los parámetros propios de <c>/correspondence/search</c>: textos, carpeta y grupos.</summary>
internal static class MailSearchParams
{
    public static readonly Error CriteriaRequired = new(
        "Correspondence.SearchCriteriaRequired",
        "Pass q, from, to or subject."
    );

    public static readonly Error InvalidFolder = new(
        "Correspondence.InvalidSearchFolder",
        $"folder must be one of: {string.Join(", ", Enum.GetNames<MailSearchFolder>())}."
    );

    public static readonly Error InvalidGroups = new(
        "Correspondence.InvalidSearchGroups",
        "groups must be a comma-separated list of: emails, files, people."
    );

    // Mismo código que siempre para el largo mínimo; el mensaje dice qué parámetro.
    public static Error TermTooShort(string parameter) =>
        new("Correspondence.SearchTermTooShort", $"{parameter} must have at least {SearchTerm.MinLength} characters.");

    /// <summary>Recorta y topa el texto; vacío = no vino. Falla si vino con menos del mínimo.</summary>
    public static bool TryText(string? raw, out string? value)
    {
        value = SearchTerm.Normalize(raw);
        return value is null || value.Length >= SearchTerm.MinLength;
    }

    /// <summary>Carpeta por nombre exacto sin distinguir mayúsculas (los números no valen).</summary>
    public static bool TryFolder(string? raw, out MailSearchFolder? folder)
    {
        folder = null;
        if (string.IsNullOrWhiteSpace(raw))
            return true;
        var name = Enum.GetNames<MailSearchFolder>()
            .FirstOrDefault(n => string.Equals(n, raw.Trim(), StringComparison.OrdinalIgnoreCase));
        if (name is null)
            return false;
        folder = Enum.Parse<MailSearchFolder>(name);
        return true;
    }

    /// <summary>Lista separada por comas; vacío = los tres. Un valor desconocido invalida todo.</summary>
    public static bool TryGroups(string? raw, out MailSearchGroups groups)
    {
        groups = MailSearchGroups.None;
        var parts = (raw ?? string.Empty).Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (parts.Length == 0)
        {
            groups = MailSearchGroups.All;
            return true;
        }
        foreach (var part in parts)
        {
            var group = part.ToLowerInvariant() switch
            {
                "emails" => MailSearchGroups.Emails,
                "files" => MailSearchGroups.Files,
                "people" => MailSearchGroups.People,
                _ => MailSearchGroups.None,
            };
            if (group == MailSearchGroups.None)
                return false;
            groups |= group;
        }
        return true;
    }
}
