using System.Text.Json;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Mapping;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Actions;

/// <summary>
/// Arma el payload del alta con el mapeo fijado en la versión, las respuestas ya depuradas y las
/// constantes de la acción. Solo viajan destinos de la operación CreateCustomer: nada fiscal ni colecciones.
/// </summary>
public static class CustomerCreationPayloadBuilder
{
    private const string PrimaryAddress = "primaryAddress";

    public static Result<CustomerCreationFromFormPayload> Build(
        FormRuleSets rules,
        FormAction action,
        MappingCatalogSnapshot catalog,
        FormAnswers answers
    )
    {
        var operation = catalog.FindOperation(FormActionNames.CreateCustomer);
        if (operation is null)
            return Unavailable();
        var includeAddress = WantsPrimaryAddress(action.Config);
        var values = new List<ClientFormTargetValue>();
        var mapped = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mapping in rules.Mappings)
        {
            var target = catalog.FindTarget(mapping.TargetKey);
            if (!Carries(target, operation.Name, includeAddress))
                continue;
            var value = ReadValue(mapping, target!, AnswersValue(answers, mapping.ComponentId));
            if (value.IsFailure)
                return Result.Failure<CustomerCreationFromFormPayload>(value.Error);
            if (value.Value is not null && mapped.Add(target!.TargetKey))
                values.Add(value.Value);
        }
        AddConstants(action.Config, operation, catalog, mapped, values);
        return Result.Success(new CustomerCreationFromFormPayload(values));
    }

    private static bool Carries(MappingTarget? target, string operation, bool includeAddress) =>
        target is not null
        && target.Status != MappingTargetStatus.Retired
        && target.Operations.Contains(operation, StringComparer.Ordinal)
        && target.DataType is not (MappingTargetType.TaxId or MappingTargetType.Collection)
        && (includeAddress || target.DataType != MappingTargetType.Address);

    // La dirección mapeada solo se guarda si la acción lo pide en postCreate.
    private static bool WantsPrimaryAddress(JsonElement config) =>
        config.ValueKind == JsonValueKind.Object
        && config.TryGetProperty("postCreate", out var postCreate)
        && postCreate.ValueKind == JsonValueKind.Array
        && postCreate
            .EnumerateArray()
            .Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == PrimaryAddress);

    private static JsonElement AnswersValue(FormAnswers answers, string componentId) =>
        answers.Values.TryGetValue(componentId, out var value) ? value : default;

    // Null cuando el campo quedó sin responder: el destino simplemente no viaja.
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
                ? Scalar(target, value.ValueKind == JsonValueKind.True ? "true" : "false")
                : Mismatch(),
            MappingTargetType.Address => ReadAddress(target, value),
            MappingTargetType.Enum => ReadOption(mapping, target, value),
            _ => value.ValueKind == JsonValueKind.String
                ? Scalar(target, Transform(value.GetString()!, mapping.Transform))
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
        var mapped = mapping.OptionMap is null ? option : mapping.OptionMap.GetValueOrDefault(option);
        return mapped is not null && target.AllowedValues.Contains(mapped, StringComparer.Ordinal)
            ? Scalar(target, mapped)
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

    private static void AddConstants(
        JsonElement config,
        MappingOperation operation,
        MappingCatalogSnapshot catalog,
        ISet<string> mapped,
        List<ClientFormTargetValue> values
    )
    {
        if (config.ValueKind != JsonValueKind.Object)
            return;
        foreach (var (configKey, targetKey) in operation.ConfigTargets)
        {
            var target = catalog.FindTarget(targetKey);
            if (target is null || mapped.Contains(targetKey) || !TryConstant(config, configKey, out var constant))
                continue;
            mapped.Add(targetKey);
            values.Add(new ClientFormTargetValue(targetKey, target.TargetSchemaVersion, constant));
        }
    }

    private static bool TryConstant(JsonElement config, string key, out string value)
    {
        value = string.Empty;
        if (
            !config.TryGetProperty(key, out var source)
            || source.ValueKind != JsonValueKind.Object
            || !source.TryGetProperty("constant", out var constant)
            || constant.ValueKind != JsonValueKind.String
        )
            return false;
        value = constant.GetString()!;
        return !string.IsNullOrWhiteSpace(value);
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

    private static Result<ClientFormTargetValue?> Scalar(MappingTarget target, string value) =>
        Result.Success<ClientFormTargetValue?>(
            string.IsNullOrWhiteSpace(value)
                ? null
                : new ClientFormTargetValue(target.TargetKey, target.TargetSchemaVersion, value)
        );

    private static Result<ClientFormTargetValue?> Mismatch() => Unavailable<ClientFormTargetValue?>();

    private static Result<CustomerCreationFromFormPayload> Unavailable() =>
        Unavailable<CustomerCreationFromFormPayload>();

    private static Result<T> Unavailable<T>() =>
        Result.Failure<T>(
            new(FormActionRunErrors.PayloadUnavailableCode, "The form answers could not be mapped to the customer.")
        );
}
