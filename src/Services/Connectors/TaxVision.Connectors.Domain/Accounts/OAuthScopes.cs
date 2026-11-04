namespace TaxVision.Connectors.Domain.Accounts;

/// <summary>Scope OAuth2 = lista separada por espacios (RFC 6749 §3.3); se compara sin mayúsculas.</summary>
public static class OAuthScopes
{
    public static IReadOnlyList<string> Missing(string? grantedScope, string? requiredScope)
    {
        var granted = Split(grantedScope).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Split(requiredScope)
            .Where(s => !granted.Contains(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string[] Split(string? scope) =>
        (scope ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
