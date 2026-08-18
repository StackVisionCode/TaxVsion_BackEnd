using System.Text.RegularExpressions;

namespace BuildingBlocks.RateLimiting;

/// <summary>
/// Nombre canónico de una política de rate-limit — formato <c>&lt;servicio&gt;.&lt;categoría&gt;.&lt;slug&gt;</c>
/// en snake_case (Plan_Implementacion_Fases.md §6.1), p.ej. <c>"auth.a.login"</c>. Validado al
/// construirse para que un typo en <see cref="RateLimitPolicyCatalog"/> falle al cargar la clase,
/// no en producción al primer request.
/// </summary>
public readonly record struct RateLimitPolicyName
{
    private static readonly Regex Pattern = new("^[a-z][a-z0-9_]*\\.[a-q]\\.[a-z][a-z0-9_]*$", RegexOptions.Compiled);

    public string Value { get; }

    private RateLimitPolicyName(string value) => Value = value;

    public static RateLimitPolicyName From(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Pattern.IsMatch(value))
        {
            throw new ArgumentException(
                $"Rate limit policy name '{value}' must match '<service>.<category>.<slug>' in snake_case "
                    + "(e.g. 'auth.a.login') — category is the lowercase single-letter id a..q.",
                nameof(value)
            );
        }

        return new RateLimitPolicyName(value);
    }

    public override string ToString() => Value;
}
