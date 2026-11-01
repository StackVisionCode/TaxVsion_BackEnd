using System.Text.Json;
using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.Tasks.ClientForms.Application.Reporting;

/// <summary>
/// Opciones de un componente reportable expresadas en los valores del índice: las de
/// <c>props.options</c> para select/radio/checkboxGroup, y <c>Yes</c>/<c>No</c> (como los guarda
/// <see cref="FormReportValueNormalizer"/>) para yesNo/checkbox, con las etiquetas del autor. Vacío = texto libre.
/// </summary>
public static class FormReportOptions
{
    public const string Yes = "Yes";
    public const string No = "No";

    public static IReadOnlyList<ComponentOption> For(FormComponent component) =>
        component.Type switch
        {
            "select" or "radio" or "checkboxGroup" => ComponentOptions.Read(component),
            "yesNo" => [new(Yes, Prop(component, "yesLabel") ?? Yes), new(No, Prop(component, "noLabel") ?? No)],
            "checkbox" => [new(Yes, Yes), new(No, No)],
            _ => [],
        };

    private static string? Prop(FormComponent component, string name) =>
        component.Props.ValueKind == JsonValueKind.Object
        && component.Props.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;
}
