using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.Tasks.ClientForms.Application.Reporting;

/// <summary>
/// Una clave reportable de un template: su etiqueta y tipo para la UI y las columnas del CSV, y sus
/// opciones (valor del índice + etiqueta) para filtrar por opción. Sin opciones = texto libre.
/// </summary>
public sealed record FormReportableKey(
    string Key,
    string Label,
    string Type,
    bool Repeatable,
    IReadOnlyList<ComponentOption> Options
);

/// <summary>
/// Lee del schema del engine qué componentes son reportables. El schema ya prohíbe <c>reportable</c>
/// en <c>taxId</c>/<c>file</c>; igual se excluyen aquí por defensa en profundidad.
/// </summary>
public static class FormReportSchema
{
    private static readonly HashSet<string> Excluded = ["taxId", "file"];

    public static IReadOnlyList<FormReportableKey> ReportableKeys(FormDefinition definition)
    {
        var keys = new List<FormReportableKey>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in definition.Pages)
        foreach (var section in page.Sections)
        {
            var repeatable = section.Repeat is not null;
            foreach (var component in section.Components)
            {
                if (!IsReportable(component) || !seen.Add(component.Key!))
                    continue;
                keys.Add(
                    new FormReportableKey(
                        component.Key!,
                        component.Label ?? component.Key!,
                        component.Type,
                        repeatable,
                        FormReportOptions.For(component)
                    )
                );
            }
        }
        return keys;
    }

    internal static bool IsReportable(FormComponent component) =>
        component.Reportable && component.Key is not null && !Excluded.Contains(component.Type);
}
