using System.Text.Json;

namespace TaxVision.DataCollection.Engine.Definitions;

/// <summary>Una opción elegible: el valor que se guarda y la etiqueta que ve quien responde.</summary>
public sealed record ComponentOption(string Value, string Label);

/// <summary>
/// Lee <c>props.options</c> (<c>[{value,label}]</c>) de select, radio y checkboxGroup. El valor es estable
/// (<c>option1</c>…); la etiqueta es la que escribió el autor y puede cambiar entre versiones.
/// </summary>
public static class ComponentOptions
{
    public static IReadOnlyList<ComponentOption> Read(FormComponent component)
    {
        if (
            component.Props.ValueKind != JsonValueKind.Object
            || !component.Props.TryGetProperty("options", out var options)
            || options.ValueKind != JsonValueKind.Array
        )
            return [];
        var result = new List<ComponentOption>();
        foreach (var option in options.EnumerateArray())
        {
            if (
                option.ValueKind != JsonValueKind.Object
                || !option.TryGetProperty("value", out var value)
                || value.ValueKind != JsonValueKind.String
            )
                continue;
            var label =
                option.TryGetProperty("label", out var text)
                && text.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(text.GetString())
                    ? text.GetString()!.Trim()
                    : value.GetString()!;
            result.Add(new ComponentOption(value.GetString()!, label));
        }
        return result;
    }

    /// <summary>Etiqueta de un valor; si la opción ya no existe, el valor tal cual.</summary>
    public static string LabelOf(FormComponent component, string value) =>
        Read(component).FirstOrDefault(option => string.Equals(option.Value, value, StringComparison.Ordinal))?.Label
        ?? value;
}
