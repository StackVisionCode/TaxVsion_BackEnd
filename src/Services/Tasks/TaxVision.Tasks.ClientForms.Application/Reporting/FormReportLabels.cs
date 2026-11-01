using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.Tasks.ClientForms.Application.Reporting;

/// <summary>
/// Traduce el valor guardado en el índice (<c>option1</c>, <c>Yes</c>) a la etiqueta que vio el cliente.
/// Las versiones publicadas no cambian, así que la versión de cada fila da la etiqueta exacta; el
/// resumen, que agrupa todas las versiones, usa la más nueva que tenga ese valor. Texto libre = el valor.
/// </summary>
public sealed class FormReportLabels
{
    // versión → clave → valor → etiqueta
    private readonly IReadOnlyDictionary<
        int,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>
    > _byVersion;
    private readonly IReadOnlyList<int> _newestFirst;

    private FormReportLabels(
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> byVersion
    )
    {
        _byVersion = byVersion;
        _newestFirst = byVersion.Keys.OrderByDescending(version => version).ToArray();
    }

    public static FormReportLabels Empty { get; } =
        new(new Dictionary<int, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>());

    public static FormReportLabels From(IEnumerable<(int VersionNumber, FormDefinition Definition)> versions)
    {
        var byVersion = new Dictionary<int, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>();
        foreach (var (versionNumber, definition) in versions)
        {
            var byKey = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            foreach (var component in definition.Pages.SelectMany(page => page.Sections).SelectMany(s => s.Components))
            {
                if (!FormReportSchema.IsReportable(component) || byKey.ContainsKey(component.Key!))
                    continue;
                var options = FormReportOptions.For(component);
                if (options.Count > 0)
                    byKey[component.Key!] = options
                        .GroupBy(option => option.Value, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.First().Label, StringComparer.Ordinal);
            }
            byVersion[versionNumber] = byKey;
        }
        return new FormReportLabels(byVersion);
    }

    /// <summary>Etiqueta en la versión que contestó el cliente; si esa versión no la tiene, la más nueva.</summary>
    public string Of(int versionNumber, string key, string value) =>
        Find(versionNumber, key, value) ?? Latest(key, value);

    public string Latest(string key, string value)
    {
        foreach (var version in _newestFirst)
        {
            var label = Find(version, key, value);
            if (label is not null)
                return label;
        }
        return value;
    }

    private string? Find(int versionNumber, string key, string value) =>
        _byVersion.TryGetValue(versionNumber, out var byKey)
        && byKey.TryGetValue(key, out var labels)
        && labels.TryGetValue(value, out var label)
            ? label
            : null;
}
