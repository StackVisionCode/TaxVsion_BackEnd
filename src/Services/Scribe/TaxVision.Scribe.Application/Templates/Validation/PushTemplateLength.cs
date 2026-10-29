using System.Text.RegularExpressions;
using TaxVision.Scribe.Domain.Templates;

namespace TaxVision.Scribe.Application.Templates.Validation;

/// <summary>
/// Largo máximo que puede tener un texto de push ya renderizado (P-21.6): la rama más larga de cada
/// condicional y cada variable a su largo máximo declarado. Una variable de texto declara su largo con
/// el valor por defecto; sin él, con un tag o un filtro que no se puede acotar, devuelve null. Los filtros
/// cuentan: append/prepend suman su literal, default toma el mayor, truncate pone tope.
/// </summary>
internal static partial class PushTemplateLength
{
    public static int? WorstCase(string source, IEnumerable<TemplateVariableDefinition> variables)
    {
        var bounds = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in variables)
            bounds[variable.Name] = Bound(variable);
        var tokens = TokenPattern().Split(source);
        var index = 0;
        var (length, stop) = Measure(tokens, ref index, bounds);
        return stop is null ? length : null;
    }

    private static int? Bound(TemplateVariableDefinition variable) =>
        variable.Type switch
        {
            VariableType.Bool => 5,
            VariableType.Number => 20,
            VariableType.Date => 32,
            VariableType.String when !string.IsNullOrEmpty(variable.DefaultValue) => variable.DefaultValue.Length,
            _ => null,
        };

    // Devuelve el largo hasta el final o hasta un tag que cierra la rama (Stop). Length null = no acotable.
    private static (int? Length, string? Stop) Measure(
        string[] tokens,
        ref int index,
        IReadOnlyDictionary<string, int?> bounds
    )
    {
        var total = 0;
        while (index < tokens.Length)
        {
            var token = tokens[index++];
            if (token.StartsWith("{{", StringComparison.Ordinal))
            {
                if (OutputBound(token, bounds) is not { } bound)
                    return (null, null);
                total += bound;
                continue;
            }
            if (!token.StartsWith("{%", StringComparison.Ordinal))
            {
                total += token.Length;
                continue;
            }
            var tag = TagPattern().Match(token).Groups[1].Value;
            switch (tag)
            {
                case "if" or "unless" or "case":
                    var branch = MeasureBranches(tokens, ref index, bounds, $"end{tag}");
                    if (branch is null)
                        return (null, null);
                    total += branch.Value;
                    break;
                case "elsif" or "else" or "when" or "endif" or "endunless" or "endcase":
                    return (total, tag);
                default:
                    return (null, null);
            }
        }
        return (total, null);
    }

    // Largo máximo de una salida {{ valor | filtro: arg }}. Solo se aceptan filtros cuyo efecto se puede
    // acotar; cualquier otro (o un argumento que no es literal) la deja sin cota.
    private static int? OutputBound(string token, IReadOnlyDictionary<string, int?> bounds)
    {
        var parts = OutputBodyPattern().Match(token).Groups[1].Value.Split('|');
        var head = parts[0].Trim();
        int? length = LiteralLength(head) ?? (bounds.TryGetValue(head, out var bound) ? bound : null);
        foreach (var part in parts.Skip(1))
        {
            var filter = part.Split(':', 2);
            var name = filter[0].Trim();
            var argument = filter.Length > 1 ? filter[1].Trim() : null;
            // Un largo todavía desconocido (null) puede acotarlo un truncate posterior; un filtro desconocido no.
            switch (name)
            {
                case "upcase" or "downcase" or "capitalize" or "strip" or "lstrip" or "rstrip":
                    break;
                case "default" when LiteralLength(argument) is { } fallback:
                    length = length is null ? null : Math.Max(length.Value, fallback);
                    break;
                case "append" or "prepend" when LiteralLength(argument) is { } extra:
                    length += extra;
                    break;
                case "truncate" when TruncateLimit(argument) is { } limit:
                    length = Math.Min(length ?? limit, limit);
                    break;
                default:
                    return null;
            }
        }
        return length;
    }

    private static int? LiteralLength(string? value) =>
        value is { Length: >= 2 } && (value[0] is '\'' or '"') && value[^1] == value[0] ? value.Length - 2 : null;

    // truncate sin argumento corta en 50 (Liquid); con argumento, en ese número.
    private static int? TruncateLimit(string? argument) =>
        argument is null ? 50
        : int.TryParse(argument.Split(',')[0].Trim(), out var limit) && limit >= 0 ? limit
        : null;

    private static int? MeasureBranches(
        string[] tokens,
        ref int index,
        IReadOnlyDictionary<string, int?> bounds,
        string end
    )
    {
        var longest = 0;
        while (true)
        {
            var (length, stop) = Measure(tokens, ref index, bounds);
            if (length is null || stop is null)
                return null;
            longest = Math.Max(longest, length.Value);
            if (stop == end)
                return longest;
            if (stop.StartsWith("end", StringComparison.Ordinal))
                return null;
        }
    }

    [GeneratedRegex(@"(\{\{.*?\}\}|\{%.*?%\})", RegexOptions.Singleline)]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"^\{\{-?(.*?)-?\}\}$", RegexOptions.Singleline)]
    private static partial Regex OutputBodyPattern();

    [GeneratedRegex(@"^\{%-?\s*([a-zA-Z_]+)")]
    private static partial Regex TagPattern();
}
