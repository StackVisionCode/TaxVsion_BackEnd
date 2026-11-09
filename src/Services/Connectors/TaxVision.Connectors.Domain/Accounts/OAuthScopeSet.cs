namespace TaxVision.Connectors.Domain.Accounts;

/// <summary>Scope OAuth2 = lista separada por espacios (RFC 6749 §3.3); se compara sin mayúsculas.</summary>
public sealed class OAuthScopeSet
{
    private readonly IReadOnlyList<string> _scopes;

    private OAuthScopeSet(IReadOnlyList<string> scopes) => _scopes = scopes;

    public static OAuthScopeSet Parse(string? scope) =>
        new(
            (scope ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        );

    public bool Contains(string scope) => _scopes.Contains(scope, StringComparer.OrdinalIgnoreCase);

    /// <summary>Los de <paramref name="required"/> que este conjunto no tiene; vacío = permisos al día.</summary>
    public IReadOnlyList<string> Missing(OAuthScopeSet required) =>
        required._scopes.Where(scope => !Contains(scope)).ToList();
}
