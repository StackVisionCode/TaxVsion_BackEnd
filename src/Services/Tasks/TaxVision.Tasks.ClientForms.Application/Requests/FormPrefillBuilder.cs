using System.Text.Json;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.Tasks.ClientForms.Application.Requests;

// P-13.1 — Arma el draft prellenado para la versión nueva sin reescribir (WCAG 3.3.7):
// copia por componentId (o por key si no coincide) desde la última request Completed, y en repeaters
// genera filas con rowId NUEVOS. Nunca copia taxId ni file. customer.displayName se llena del directorio.
public static class FormPrefillBuilder
{
    private const string DisplayNameSource = "customer.displayName";

    private static bool IsPrefillable(string type) =>
        !string.Equals(type, "taxId", StringComparison.Ordinal)
        && !string.Equals(type, "file", StringComparison.Ordinal);

    public static (FormAnswers Draft, IReadOnlyList<string> PrefilledComponentIds) Build(
        FormDefinition source,
        FormAnswers sourceAnswers,
        FormDefinition target,
        string? customerDisplayName
    )
    {
        var sourceById = new Dictionary<string, SourceValue>(StringComparer.Ordinal);
        var sourceByKey = new Dictionary<string, SourceValue>(StringComparer.Ordinal);
        var sourceRepeaters = new Dictionary<string, SourceRepeater>(StringComparer.Ordinal);
        IndexSource(source, sourceAnswers, sourceById, sourceByKey, sourceRepeaters);

        var draft = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var prefilled = new List<string>();
        foreach (var section in target.Pages.SelectMany(page => page.Sections))
        {
            if (section.Repeat is null)
                CopyRegular(section, sourceById, sourceByKey, customerDisplayName, draft, prefilled);
            else
                CopyRepeater(section, sourceRepeaters, draft, prefilled);
        }

        return (new FormAnswers { SchemaVersion = sourceAnswers.SchemaVersion, Values = draft }, prefilled);
    }

    private static void CopyRegular(
        FormSection section,
        IReadOnlyDictionary<string, SourceValue> sourceById,
        IReadOnlyDictionary<string, SourceValue> sourceByKey,
        string? customerDisplayName,
        IDictionary<string, JsonElement> draft,
        ICollection<string> prefilled
    )
    {
        foreach (var component in section.Components)
        {
            if (!IsPrefillable(component.Type))
                continue;
            if (string.Equals(component.Prefill?.Source, DisplayNameSource, StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(customerDisplayName))
                {
                    draft[component.Id] = JsonSerializer.SerializeToElement(customerDisplayName);
                    prefilled.Add(component.Id);
                }
                continue;
            }
            if (TryMatch(component, sourceById, sourceByKey, out var value))
            {
                draft[component.Id] = value.Clone();
                prefilled.Add(component.Id);
            }
        }
    }

    private static void CopyRepeater(
        FormSection section,
        IReadOnlyDictionary<string, SourceRepeater> sourceRepeaters,
        IDictionary<string, JsonElement> draft,
        ICollection<string> prefilled
    )
    {
        if (!sourceRepeaters.TryGetValue(section.Id, out var sourceRepeater))
            return;
        var rows = new List<object>();
        foreach (var sourceRow in sourceRepeater.Rows)
        {
            var rowValues = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var component in section.Components)
            {
                if (!IsPrefillable(component.Type))
                    continue;
                if (TryMatchRow(component, sourceRepeater, sourceRow, out var value))
                    rowValues[component.Id] = value.Clone();
            }
            if (rowValues.Count > 0)
                rows.Add(new { rowId = Guid.NewGuid().ToString("N"), values = rowValues });
        }
        if (rows.Count > 0)
        {
            draft[section.Id] = JsonSerializer.SerializeToElement(new { rows });
            prefilled.Add(section.Id);
        }
    }

    private static bool TryMatch(
        FormComponent component,
        IReadOnlyDictionary<string, SourceValue> sourceById,
        IReadOnlyDictionary<string, SourceValue> sourceByKey,
        out JsonElement value
    )
    {
        if (sourceById.TryGetValue(component.Id, out var byId) && SameType(byId.Type, component.Type))
        {
            value = byId.Value;
            return true;
        }
        if (
            !string.IsNullOrWhiteSpace(component.Key)
            && sourceByKey.TryGetValue(component.Key, out var byKey)
            && SameType(byKey.Type, component.Type)
        )
        {
            value = byKey.Value;
            return true;
        }
        value = default;
        return false;
    }

    private static bool TryMatchRow(
        FormComponent component,
        SourceRepeater sourceRepeater,
        SourceRow sourceRow,
        out JsonElement value
    )
    {
        if (
            sourceRepeater.ComponentTypesById.TryGetValue(component.Id, out var typeById)
            && SameType(typeById, component.Type)
            && sourceRow.Values.TryGetValue(component.Id, out var byId)
        )
        {
            value = byId;
            return true;
        }
        if (
            !string.IsNullOrWhiteSpace(component.Key)
            && sourceRepeater.ComponentIdByKey.TryGetValue(component.Key, out var sourceId)
            && sourceRepeater.ComponentTypesById.TryGetValue(sourceId, out var typeByKey)
            && SameType(typeByKey, component.Type)
            && sourceRow.Values.TryGetValue(sourceId, out var byKey)
        )
        {
            value = byKey;
            return true;
        }
        value = default;
        return false;
    }

    private static bool SameType(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);

    private static void IndexSource(
        FormDefinition source,
        FormAnswers sourceAnswers,
        IDictionary<string, SourceValue> sourceById,
        IDictionary<string, SourceValue> sourceByKey,
        IDictionary<string, SourceRepeater> sourceRepeaters
    )
    {
        foreach (var section in source.Pages.SelectMany(page => page.Sections))
        {
            if (section.Repeat is null)
            {
                foreach (var component in section.Components)
                {
                    if (!sourceAnswers.Values.TryGetValue(component.Id, out var value))
                        continue;
                    var entry = new SourceValue(component.Type, value);
                    sourceById[component.Id] = entry;
                    if (!string.IsNullOrWhiteSpace(component.Key))
                        sourceByKey.TryAdd(component.Key, entry);
                }
                continue;
            }
            sourceRepeaters[section.Id] = BuildSourceRepeater(section, sourceAnswers);
        }
    }

    private static SourceRepeater BuildSourceRepeater(FormSection section, FormAnswers sourceAnswers)
    {
        var typesById = new Dictionary<string, string>(StringComparer.Ordinal);
        var idByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var component in section.Components)
        {
            typesById[component.Id] = component.Type;
            if (!string.IsNullOrWhiteSpace(component.Key))
                idByKey.TryAdd(component.Key, component.Id);
        }
        var rows = new List<SourceRow>();
        foreach (var row in AnswersRows(sourceAnswers, section.Id))
            rows.Add(row);
        return new SourceRepeater(typesById, idByKey, rows);
    }

    private static IEnumerable<SourceRow> AnswersRows(FormAnswers answers, string sectionId)
    {
        if (
            !answers.Values.TryGetValue(sectionId, out var raw)
            || raw.ValueKind != JsonValueKind.Object
            || !raw.TryGetProperty("rows", out var rows)
            || rows.ValueKind != JsonValueKind.Array
        )
            yield break;
        foreach (var row in rows.EnumerateArray())
        {
            if (!row.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Object)
                continue;
            var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in values.EnumerateObject())
                map[property.Name] = property.Value;
            yield return new SourceRow(map);
        }
    }

    private sealed record SourceValue(string Type, JsonElement Value);

    private sealed record SourceRepeater(
        IReadOnlyDictionary<string, string> ComponentTypesById,
        IReadOnlyDictionary<string, string> ComponentIdByKey,
        IReadOnlyList<SourceRow> Rows
    );

    private sealed record SourceRow(IReadOnlyDictionary<string, JsonElement> Values);
}
