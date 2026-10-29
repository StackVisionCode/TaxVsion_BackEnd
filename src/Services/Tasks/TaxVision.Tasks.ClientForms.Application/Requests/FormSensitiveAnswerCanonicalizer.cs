using System.Text.Json;
using System.Text.Json.Nodes;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests;

// last4 y kind de un taxId salen de la fila del servidor: lo que mande el cliente no se guarda.
internal static class FormSensitiveAnswerCanonicalizer
{
    internal static FormAnswers Apply(FormDefinition definition, FormAnswers answers, FormRequest request)
    {
        var answersById = request.Submission?.SensitiveAnswers.ToDictionary(item => item.Id);
        if (answersById is null || answersById.Count == 0)
            return answers;
        var values = new Dictionary<string, JsonElement>(answers.Values, StringComparer.Ordinal);
        foreach (var section in definition.Pages.SelectMany(page => page.Sections))
        {
            var taxIds = section.Components.Where(item => item.Type == "taxId").Select(item => item.Id).ToArray();
            if (taxIds.Length == 0)
                continue;
            if (section.Repeat is null)
            {
                foreach (var componentId in taxIds)
                    if (values.TryGetValue(componentId, out var value))
                        values[componentId] = Canonical(value, componentId, null, answersById);
            }
            else if (values.TryGetValue(section.Id, out var raw))
                values[section.Id] = CanonicalRows(raw, taxIds, answersById);
        }
        return answers.WithValues(values);
    }

    private static JsonElement CanonicalRows(
        JsonElement raw,
        IReadOnlyList<string> taxIds,
        IReadOnlyDictionary<Guid, FormSensitiveAnswer> answersById
    )
    {
        if (JsonNode.Parse(raw.GetRawText()) is not JsonObject section || section["rows"] is not JsonArray rows)
            return raw;
        foreach (var row in rows.OfType<JsonObject>())
        {
            if (row["values"] is not JsonObject values)
                continue;
            var rowId = row["rowId"] is JsonValue id && id.TryGetValue<string>(out var text) ? text : null;
            foreach (var componentId in taxIds)
            {
                if (values[componentId] is not { } current)
                    continue;
                var canonical = Canonical(JsonSerializer.SerializeToElement(current), componentId, rowId, answersById);
                values[componentId] = JsonNode.Parse(canonical.GetRawText());
            }
        }
        return JsonSerializer.SerializeToElement(section);
    }

    private static JsonElement Canonical(
        JsonElement value,
        string componentId,
        string? rowId,
        IReadOnlyDictionary<Guid, FormSensitiveAnswer> answersById
    )
    {
        if (
            value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("sensitiveRef", out var node)
            || node.ValueKind != JsonValueKind.String
            || !node.TryGetGuid(out var answerId)
            || !answersById.TryGetValue(answerId, out var answer)
            || answer.ComponentId != componentId
            || answer.RowId != rowId
        )
            return value;
        return JsonSerializer.SerializeToElement(
            new JsonObject
            {
                ["sensitiveRef"] = answer.Id,
                ["last4"] = answer.Protected.Last4,
                ["kind"] = answer.Kind.ToString().ToLowerInvariant(),
            }
        );
    }
}
