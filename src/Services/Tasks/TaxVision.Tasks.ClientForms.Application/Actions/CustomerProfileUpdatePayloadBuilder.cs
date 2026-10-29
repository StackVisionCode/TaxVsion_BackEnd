using System.Text.Json;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Mapping;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Actions;

/// <summary>
/// P-13.2 — Arma los destinos de UpdateCustomerProfile desde el mapeo fijado y las respuestas depuradas,
/// filtrando por los componentes elegidos. El taxId NO se arma aquí: viaja aparte (respuesta sensible) y
/// solo si el actor lo pidió. Devuelve además si hay destinos fiscales para el gate de admin del handler.
/// </summary>
public static class CustomerProfileUpdatePayloadBuilder
{
    public sealed record ProfileUpdateDraft(
        IReadOnlyList<ClientFormTargetValue> Values,
        IReadOnlyList<string> TargetKeys,
        bool HasFiscalTarget,
        string? TaxIdComponentId,
        int TaxIdTargetSchemaVersion
    );

    public static Result<ProfileUpdateDraft> Build(
        FormRuleSets rules,
        MappingCatalogSnapshot catalog,
        FormAnswers answers,
        IReadOnlyCollection<string>? selectedComponentIds
    )
    {
        var operation = catalog.FindOperation(FormActionNames.UpdateCustomerProfile);
        if (operation is null)
            return Unavailable();

        var values = new List<ClientFormTargetValue>();
        var targetKeys = new List<string>();
        var mapped = new HashSet<string>(StringComparer.Ordinal);
        var hasFiscal = false;
        string? taxIdComponentId = null;
        var taxIdSchema = 1;

        foreach (var mapping in rules.Mappings)
        {
            if (selectedComponentIds is not null && !selectedComponentIds.Contains(mapping.ComponentId))
                continue;
            var target = catalog.FindTarget(mapping.TargetKey);
            if (target is null || target.Status == MappingTargetStatus.Retired)
                continue;
            if (!target.Operations.Contains(operation.Name, StringComparer.Ordinal))
                continue;

            if (target.DataType == MappingTargetType.TaxId)
            {
                // El valor sensible lo resuelve el handler; aquí solo se registra el componente origen.
                taxIdComponentId = mapping.ComponentId;
                taxIdSchema = target.TargetSchemaVersion;
                continue;
            }

            if (target.RequiresTenantAdmin)
                hasFiscal = true;

            var built =
                target.DataType == MappingTargetType.Collection
                    ? BuildCollection(mapping, target, answers)
                    : ReadValue(mapping, target, AnswersValue(answers, mapping.ComponentId));
            if (built.IsFailure)
                return Result.Failure<ProfileUpdateDraft>(built.Error);
            if (built.Value is not null && mapped.Add(target.TargetKey))
            {
                values.Add(built.Value);
                targetKeys.Add(target.TargetKey);
            }
        }

        return Result.Success(new ProfileUpdateDraft(values, targetKeys, hasFiscal, taxIdComponentId, taxIdSchema));
    }

    private static Result<ClientFormTargetValue?> BuildCollection(
        FormMapping mapping,
        MappingTarget target,
        FormAnswers answers
    )
    {
        if (mapping.RowFields is null || mapping.RowFields.Count == 0)
            return Result.Success<ClientFormTargetValue?>(null);
        var rows = new List<ClientFormTargetRow>();
        foreach (var row in Rows(answers, mapping.ComponentId))
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (componentId, fieldName) in mapping.RowFields)
            {
                if (row.TryGetValue(componentId, out var value) && Scalar(value) is { } text)
                    fields[fieldName] = text;
            }
            if (fields.Count > 0)
                rows.Add(new ClientFormTargetRow(fields));
        }
        return rows.Count == 0
            ? Result.Success<ClientFormTargetValue?>(null)
            : Result.Success<ClientFormTargetValue?>(
                new ClientFormTargetValue(target.TargetKey, target.TargetSchemaVersion, Rows: rows)
            );
    }

    private static IEnumerable<IReadOnlyDictionary<string, JsonElement>> Rows(FormAnswers answers, string sectionId)
    {
        var raw = AnswersValue(answers, sectionId);
        if (
            raw.ValueKind != JsonValueKind.Object
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
            yield return map;
        }
    }

    private static string? Scalar(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };

    private static JsonElement AnswersValue(FormAnswers answers, string componentId) =>
        answers.Values.TryGetValue(componentId, out var value) ? value : default;

    private static Result<ClientFormTargetValue?> ReadValue(
        FormMapping mapping,
        MappingTarget target,
        JsonElement value
    )
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return Result.Success<ClientFormTargetValue?>(null);
        return target.DataType switch
        {
            MappingTargetType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? ScalarValue(target, value.ValueKind == JsonValueKind.True ? "true" : "false")
                : Mismatch(),
            MappingTargetType.Address => ReadAddress(target, value),
            MappingTargetType.Enum => ReadOption(mapping, target, value),
            _ => value.ValueKind == JsonValueKind.String
                ? ScalarValue(target, Transform(value.GetString()!, mapping.Transform))
                : Mismatch(),
        };
    }

    private static Result<ClientFormTargetValue?> ReadOption(
        FormMapping mapping,
        MappingTarget target,
        JsonElement value
    )
    {
        if (value.ValueKind != JsonValueKind.String)
            return Mismatch();
        var option = value.GetString()!;
        var resolved = mapping.OptionMap is null ? option : mapping.OptionMap.GetValueOrDefault(option);
        return resolved is not null && target.AllowedValues.Contains(resolved, StringComparer.Ordinal)
            ? ScalarValue(target, resolved)
            : Mismatch();
    }

    private static Result<ClientFormTargetValue?> ReadAddress(MappingTarget target, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return Mismatch();
        var line1 = Text(value, "line1");
        var city = Text(value, "city");
        var postalCode = Text(value, "postalCode");
        if (line1 is null || city is null || postalCode is null)
            return Result.Success<ClientFormTargetValue?>(null);
        var address = new ClientFormAddressValue(
            line1,
            Text(value, "line2"),
            city,
            Text(value, "state"),
            postalCode,
            Text(value, "country") ?? "US"
        );
        return Result.Success<ClientFormTargetValue?>(
            new ClientFormTargetValue(target.TargetKey, target.TargetSchemaVersion, Address: address)
        );
    }

    private static string Transform(string value, MappingTransform transform) =>
        transform switch
        {
            MappingTransform.Trim => value.Trim(),
            MappingTransform.Lowercase => value.Trim().ToLowerInvariant(),
            MappingTransform.Uppercase => value.Trim().ToUpperInvariant(),
            MappingTransform.DigitsOnly => new string(value.Where(char.IsAsciiDigit).ToArray()),
            _ => value,
        };

    private static string? Text(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item)
        && item.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(item.GetString())
            ? item.GetString()!.Trim()
            : null;

    private static Result<ClientFormTargetValue?> ScalarValue(MappingTarget target, string value) =>
        Result.Success<ClientFormTargetValue?>(
            string.IsNullOrWhiteSpace(value)
                ? null
                : new ClientFormTargetValue(target.TargetKey, target.TargetSchemaVersion, value)
        );

    private static Result<ClientFormTargetValue?> Mismatch() =>
        Result.Failure<ClientFormTargetValue?>(
            new(FormActionRunErrors.PayloadUnavailableCode, "The form answers could not be mapped to the profile.")
        );

    private static Result<ProfileUpdateDraft> Unavailable() =>
        Result.Failure<ProfileUpdateDraft>(
            new(FormActionRunErrors.PayloadUnavailableCode, "The form answers could not be mapped to the profile.")
        );
}
