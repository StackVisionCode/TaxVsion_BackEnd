using System.Text.Json;
using System.Text.RegularExpressions;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Components;
using TaxVision.DataCollection.Engine.Conditions;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Formats;
using TaxVision.DataCollection.Engine.Visibility;

namespace TaxVision.DataCollection.Engine.Validation;

internal sealed partial class AnswersValidator(
    DataCollectionLimits limits,
    ComponentRegistry registry,
    TimeProvider timeProvider
)
{
    internal AnswersValidationReport Validate(
        FormDefinition definition,
        FormAnswers answers,
        VisibilityMap visibility,
        AnswersValidationMode mode
    )
    {
        var issues = new List<ValidationIssue>();
        if (answers.SchemaVersion != 1)
            Add(
                issues,
                "definition.unsupported_schema_version",
                "/schemaVersion",
                null,
                null,
                "Only schema version 1 is supported."
            );

        var index = DefinitionIndex.Create(definition, registry);
        ValidateRootKeys(answers, index, issues);
        foreach (var page in definition.Pages)
        {
            if (!ShouldValidate(visibility.IsVisible(page.Id), mode))
                continue;
            foreach (var section in page.Sections)
            {
                if (!ShouldValidate(visibility.IsVisible(section.Id), mode))
                    continue;
                if (section.Repeat is null)
                    ValidateRegularSection(section, definition, answers, index, visibility, mode, issues);
                else
                    ValidateRepeater(section, definition, answers, index, visibility, mode, issues);
            }
        }
        return new AnswersValidationReport(issues);
    }

    // El borrador conserva lo oculto, así que lo valida igual, sin `required`; el envío solo valida lo visible.
    private static bool ShouldValidate(bool visible, AnswersValidationMode mode) =>
        visible || mode == AnswersValidationMode.Draft;

    private static void ValidateRootKeys(
        FormAnswers answers,
        DefinitionIndex index,
        ICollection<ValidationIssue> issues
    )
    {
        foreach (var pair in answers.Values)
        {
            var knownComponent =
                index.Components.TryGetValue(pair.Key, out var component)
                && component.RepeaterSectionId is null
                && component.ComponentType?.Kind == ComponentKind.Input;
            var knownRepeater =
                index.Sections.TryGetValue(pair.Key, out var section) && section.Section.Repeat is not null;
            if (!knownComponent && !knownRepeater)
                Add(
                    issues,
                    "answer.unknown_component",
                    $"/values/{pair.Key}",
                    pair.Key,
                    null,
                    "Answer key is not part of this definition version."
                );
        }
    }

    private void ValidateRegularSection(
        FormSection section,
        FormDefinition definition,
        FormAnswers answers,
        DefinitionIndex index,
        VisibilityMap visibility,
        AnswersValidationMode mode,
        ICollection<ValidationIssue> issues
    )
    {
        foreach (var component in section.Components)
        {
            if (
                !ShouldValidate(visibility.IsVisible(component.Id), mode)
                || !registry.TryGet(component.Type, out var type)
                || type.Kind != ComponentKind.Input
            )
                continue;
            var value = AnswersReader.Get(answers.Values, component.Id);
            ValidateComponent(
                component,
                value,
                $"/values/{component.Id}",
                definition,
                answers,
                index,
                visibility,
                mode,
                null,
                null,
                issues
            );
        }
    }

    private void ValidateRepeater(
        FormSection section,
        FormDefinition definition,
        FormAnswers answers,
        DefinitionIndex index,
        VisibilityMap visibility,
        AnswersValidationMode mode,
        ICollection<ValidationIssue> issues
    )
    {
        var raw = AnswersReader.Get(answers.Values, section.Id);
        if (raw.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            if (mode == AnswersValidationMode.Submit && section.Repeat!.MinRows > 0)
                Add(
                    issues,
                    "answer.rows_below_min",
                    $"/values/{section.Id}",
                    null,
                    null,
                    "Repeater does not contain enough rows."
                );
            return;
        }
        if (!TryReadRows(raw, out var rows))
        {
            Add(
                issues,
                "answer.type_mismatch",
                $"/values/{section.Id}",
                null,
                null,
                "Repeater answer has an invalid shape."
            );
            return;
        }

        var maxRows = Math.Min(section.Repeat!.MaxRows, limits.MaxRepeaterRows);
        if (rows.Count > maxRows)
            Add(
                issues,
                "answer.rows_above_max",
                $"/values/{section.Id}/rows",
                null,
                null,
                "Repeater contains too many rows."
            );
        if (mode == AnswersValidationMode.Submit && rows.Count < section.Repeat.MinRows)
            Add(
                issues,
                "answer.rows_below_min",
                $"/values/{section.Id}/rows",
                null,
                null,
                "Repeater does not contain enough rows."
            );

        var sectionComponents = section.Components.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var rowIds = new HashSet<string>(StringComparer.Ordinal);
        // Igual que la visibilidad, las filas por encima del máximo no se recorren.
        for (var rowIndex = 0; rowIndex < Math.Min(rows.Count, maxRows); rowIndex++)
        {
            var row = rows[rowIndex];
            var rowPath = $"/values/{section.Id}/rows/{rowIndex}";
            if (!RowIdPattern().IsMatch(row.RowId))
                Add(
                    issues,
                    "answer.row_id_invalid",
                    $"{rowPath}/rowId",
                    null,
                    row.RowId,
                    "Row identifier format is invalid."
                );
            else if (!rowIds.Add(row.RowId))
                Add(
                    issues,
                    "answer.row_id_duplicate",
                    $"{rowPath}/rowId",
                    null,
                    row.RowId,
                    "Row identifier must be unique in the repeater."
                );

            foreach (var key in row.Values.Keys)
            {
                if (
                    !sectionComponents.TryGetValue(key, out var known)
                    || !registry.TryGet(known.Type, out var knownType)
                    || knownType.Kind != ComponentKind.Input
                )
                    Add(
                        issues,
                        "answer.unknown_component",
                        $"{rowPath}/values/{key}",
                        key,
                        row.RowId,
                        "Answer key is not part of this repeater."
                    );
            }

            foreach (var component in section.Components)
            {
                if (
                    !ShouldValidate(visibility.IsVisible(component.Id, row.RowId), mode)
                    || !registry.TryGet(component.Type, out var type)
                    || type.Kind != ComponentKind.Input
                )
                    continue;
                var value = AnswersReader.Get(row.Values, component.Id);
                ValidateComponent(
                    component,
                    value,
                    $"{rowPath}/values/{component.Id}",
                    definition,
                    answers,
                    index,
                    visibility,
                    mode,
                    row.RowId,
                    row.Values,
                    issues
                );
            }
        }
    }

    private void ValidateComponent(
        FormComponent component,
        JsonElement value,
        string path,
        FormDefinition definition,
        FormAnswers answers,
        DefinitionIndex index,
        VisibilityMap visibility,
        AnswersValidationMode mode,
        string? rowId,
        IReadOnlyDictionary<string, JsonElement>? rowValues,
        ICollection<ValidationIssue> issues
    )
    {
        var present = value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);
        if (present && !HasCompatibleShape(component.Type, value))
        {
            TypeMismatch(component, path, rowId, issues);
            return;
        }
        var answered = AnswersReader.IsAnswered(value, component.Type);
        // Un borrador es parcial por definición: los requeridos se exigen solo al enviar.
        var required =
            mode == AnswersValidationMode.Submit
            && (
                component.Required
                || component.RequiredWhen is not null
                    && ConditionEvaluator.Evaluate(component.RequiredWhen, answers, index, visibility, rowId, rowValues)
            );
        if (required && !answered)
        {
            Add(issues, "answer.required", path, component.Id, rowId, "A required answer is missing.");
            return;
        }
        if (!answered)
            return;

        switch (component.Type)
        {
            case "text":
                ValidateText(component, value, path, rowId, false, issues);
                break;
            case "textarea":
                ValidateText(component, value, path, rowId, true, issues);
                break;
            case "number":
                ValidateNumber(component, value, path, rowId, false, issues);
                break;
            case "currency":
                ValidateNumber(component, value, path, rowId, true, issues);
                break;
            case "date":
                ValidateDate(component, value, path, rowId, Today(), issues);
                break;
            case "email":
                ValidateEmail(component, value, path, rowId, issues);
                break;
            case "phone":
                ValidatePhone(component, value, path, rowId, issues);
                break;
            case "select" or "radio":
                ValidateChoice(component, value, path, rowId, issues);
                break;
            case "checkboxGroup":
                ValidateCheckboxGroup(component, value, path, rowId, issues);
                break;
            case "yesNo":
                ValidateBoolean(component, value, path, rowId, false, issues);
                break;
            case "checkbox":
                ValidateBoolean(component, value, path, rowId, required, issues);
                break;
            case "file":
                ValidateFiles(component, value, path, rowId, issues);
                break;
            case "address":
                ValidateAddress(component, value, path, rowId, mode, issues);
                break;
            case "taxId":
                ValidateTaxId(component, value, path, rowId, issues);
                break;
            default:
                Add(issues, "answer.type_mismatch", path, component.Id, rowId, "Answer type is not supported.");
                break;
        }
    }

    private static bool HasCompatibleShape(string type, JsonElement value) =>
        type switch
        {
            "text" or "textarea" or "number" or "currency" or "date" or "email" or "phone" or "select" or "radio" =>
                value.ValueKind == JsonValueKind.String,
            "checkboxGroup" or "file" => value.ValueKind == JsonValueKind.Array,
            "yesNo" or "checkbox" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "address" or "taxId" => value.ValueKind == JsonValueKind.Object,
            _ => false,
        };

    private static void ValidateText(
        FormComponent component,
        JsonElement value,
        string path,
        string? rowId,
        bool multiline,
        ICollection<ValidationIssue> issues
    )
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            TypeMismatch(component, path, rowId, issues);
            return;
        }
        var text = value.GetString()!.Trim();
        var max = JsonProps.GetInt(component.Props, "maxLength", multiline ? 2_000 : 200);
        var min = multiline ? 0 : JsonProps.GetInt(component.Props, "minLength", 0);
        if (text.Length < min)
            Add(issues, "answer.too_short", path, component.Id, rowId, "Answer is shorter than allowed.");
        if (text.Length > max)
            Add(issues, "answer.too_long", path, component.Id, rowId, "Answer is longer than allowed.");
        if (text.Any(character => char.IsControl(character) && (!multiline || character is not ('\r' or '\n' or '\t'))))
            Add(issues, "answer.type_mismatch", path, component.Id, rowId, "Answer contains control characters.");
        if (TaxIdentifierFormat.LooksLikeTaxIdentifier(text))
            Add(
                issues,
                "answer.looks_like_tax_identifier",
                path,
                component.Id,
                rowId,
                "For your security, enter tax IDs only in the dedicated field."
            );
        if (SensitivePatternGuard.LooksLikeCardNumber(text))
            Add(
                issues,
                "answer.looks_like_card_number",
                path,
                component.Id,
                rowId,
                "Payment card numbers cannot be entered in forms."
            );
    }

    private static void ValidateNumber(
        FormComponent component,
        JsonElement value,
        string path,
        string? rowId,
        bool currency,
        ICollection<ValidationIssue> issues
    )
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            TypeMismatch(component, path, rowId, issues);
            return;
        }
        var text = value.GetString()!;
        var pattern = currency ? CurrencyPattern() : NumberPattern();
        if (!pattern.IsMatch(text) || !PlainNumberFormat.TryParse(text, out decimal number))
        {
            TypeMismatch(component, path, rowId, issues);
            return;
        }
        var decimals = currency ? 2 : JsonProps.GetInt(component.Props, "decimals", 0);
        var actualDecimals = text.Contains('.', StringComparison.Ordinal) ? text[(text.IndexOf('.') + 1)..].Length : 0;
        if (actualDecimals > decimals)
            Add(issues, "answer.too_many_decimals", path, component.Id, rowId, "Answer has too many decimal places.");
        var min = JsonProps.GetDecimal(component.Props, "min") ?? (currency ? 0 : null);
        var max = JsonProps.GetDecimal(component.Props, "max");
        if (currency && number < 0 && !JsonProps.GetBool(component.Props, "allowNegative", false))
            Add(issues, "answer.out_of_range", path, component.Id, rowId, "Negative currency is not allowed.");
        if ((min.HasValue && number < min) || (max.HasValue && number > max))
            Add(issues, "answer.out_of_range", path, component.Id, rowId, "Answer is outside the allowed range.");
    }

    private static void ValidateDate(
        FormComponent component,
        JsonElement value,
        string path,
        string? rowId,
        DateOnly today,
        ICollection<ValidationIssue> issues
    )
    {
        if (value.ValueKind != JsonValueKind.String || !DateFormat.TryParse(value.GetString(), out var date))
        {
            Add(issues, "answer.invalid_date", path, component.Id, rowId, "Date is invalid.");
            return;
        }
        var min = ReadDateBound(component, "min", today);
        var max = ReadDateBound(component, "max", today);
        if ((min.HasValue && date < min) || (max.HasValue && date > max))
            Add(issues, "answer.out_of_range", path, component.Id, rowId, "Date is outside the allowed range.");
    }

    // "today" = fecha UTC del reloj inyectado al validar (P-1.4).
    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    private static DateOnly? ReadDateBound(FormComponent component, string name, DateOnly today)
    {
        var bound = JsonProps.GetString(component.Props, name);
        if (bound == "today")
            return today;
        return DateFormat.TryParse(bound, out var value) ? value : null;
    }

    private static void ValidateEmail(
        FormComponent component,
        JsonElement value,
        string path,
        string? rowId,
        ICollection<ValidationIssue> issues
    )
    {
        if (
            value.ValueKind != JsonValueKind.String
            || !EmailFormat.IsValid(value.GetString()!.Trim().ToLowerInvariant())
        )
            Add(issues, "answer.invalid_email", path, component.Id, rowId, "Email address is invalid.");
    }

    private static void ValidatePhone(
        FormComponent component,
        JsonElement value,
        string path,
        string? rowId,
        ICollection<ValidationIssue> issues
    )
    {
        if (value.ValueKind != JsonValueKind.String || !PhoneFormat.IsValid(value.GetString()!))
            Add(issues, "answer.invalid_phone", path, component.Id, rowId, "Phone number must use E.164 format.");
    }

    private static void ValidateChoice(
        FormComponent component,
        JsonElement value,
        string path,
        string? rowId,
        ICollection<ValidationIssue> issues
    )
    {
        if (
            value.ValueKind != JsonValueKind.String
            || !JsonProps.OptionValues(component.Props).Contains(value.GetString()!)
        )
            Add(issues, "answer.invalid_option", path, component.Id, rowId, "Selected option is invalid.");
    }

    private static void ValidateCheckboxGroup(
        FormComponent component,
        JsonElement value,
        string path,
        string? rowId,
        ICollection<ValidationIssue> issues
    )
    {
        if (
            value.ValueKind != JsonValueKind.Array
            || value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String)
        )
        {
            TypeMismatch(component, path, rowId, issues);
            return;
        }
        var selected = value.EnumerateArray().Select(item => item.GetString()!).ToArray();
        var allowed = JsonProps.OptionValues(component.Props);
        if (
            selected.Distinct(StringComparer.Ordinal).Count() != selected.Length
            || selected.Any(item => !allowed.Contains(item))
        )
            Add(
                issues,
                "answer.invalid_option",
                path,
                component.Id,
                rowId,
                "One or more selected options are invalid."
            );
        var min = JsonProps.GetInt(component.Props, "minSelected", 0);
        var max = JsonProps.GetInt(component.Props, "maxSelected", allowed.Count);
        if (selected.Length < min || selected.Length > max || (component.Required && selected.Length == 0))
            Add(
                issues,
                "answer.selection_count",
                path,
                component.Id,
                rowId,
                "Selection count is outside the allowed range."
            );
    }

    private static void ValidateBoolean(
        FormComponent component,
        JsonElement value,
        string path,
        string? rowId,
        bool mustBeTrue,
        ICollection<ValidationIssue> issues
    )
    {
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            TypeMismatch(component, path, rowId, issues);
        else if (mustBeTrue && value.ValueKind != JsonValueKind.True)
            Add(
                issues,
                "answer.must_be_checked",
                path,
                component.Id,
                rowId,
                "Required acknowledgement must be checked."
            );
    }

    private void ValidateFiles(
        FormComponent component,
        JsonElement value,
        string path,
        string? rowId,
        ICollection<ValidationIssue> issues
    )
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            TypeMismatch(component, path, rowId, issues);
            return;
        }
        var maxFiles = Math.Min(JsonProps.GetInt(component.Props, "maxFiles", 5), limits.MaxFilesPerField);
        if (value.GetArrayLength() > maxFiles)
            Add(issues, "answer.too_many_files", path, component.Id, rowId, "Too many files were attached.");
        var ids = new HashSet<Guid>();
        foreach (var item in value.EnumerateArray())
        {
            if (
                item.ValueKind != JsonValueKind.Object
                || item.EnumerateObject().Count() != 1
                || !item.TryGetProperty("fileId", out var idNode)
                || idNode.ValueKind != JsonValueKind.String
                || !Guid.TryParse(idNode.GetString(), out var id)
                || !ids.Add(id)
            )
                TypeMismatch(component, path, rowId, issues);
        }
    }

    private static void ValidateAddress(
        FormComponent component,
        JsonElement value,
        string path,
        string? rowId,
        AnswersValidationMode mode,
        ICollection<ValidationIssue> issues
    )
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            TypeMismatch(component, path, rowId, issues);
            return;
        }
        var allowed = new HashSet<string>(
            ["line1", "line2", "city", "state", "postalCode", "country"],
            StringComparer.Ordinal
        );
        if (
            value
                .EnumerateObject()
                .Any(property =>
                    !allowed.Contains(property.Name)
                    || property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
                )
        )
        {
            TypeMismatch(component, path, rowId, issues);
            return;
        }
        var line1 = ReadString(value, "line1");
        var line2 = ReadString(value, "line2");
        var city = ReadString(value, "city");
        var state = ReadString(value, "state");
        var postal = ReadString(value, "postalCode");
        var country = ReadString(value, "country");
        if (
            mode == AnswersValidationMode.Submit
            && (
                string.IsNullOrWhiteSpace(line1)
                || string.IsNullOrWhiteSpace(city)
                || string.IsNullOrWhiteSpace(state)
                || string.IsNullOrWhiteSpace(postal)
            )
        )
            Add(issues, "answer.required", path, component.Id, rowId, "Address is incomplete.");
        if (line1?.Length > 100 || line2?.Length > 100 || city?.Length > 60)
            Add(issues, "answer.too_long", path, component.Id, rowId, "Address contains a value that is too long.");
        if (state is not null && !UsStateCatalog.Contains(state))
            Add(issues, "answer.invalid_state", $"{path}/state", component.Id, rowId, "State code is invalid.");
        if (postal is not null && !PostalCodeFormat.IsValid(postal))
            Add(
                issues,
                "answer.invalid_postal_code",
                $"{path}/postalCode",
                component.Id,
                rowId,
                "Postal code is invalid."
            );
        if (country is not null && country != "US")
            Add(
                issues,
                "answer.type_mismatch",
                $"{path}/country",
                component.Id,
                rowId,
                "Only US addresses are supported in v1."
            );
    }

    private static void ValidateTaxId(
        FormComponent component,
        JsonElement value,
        string path,
        string? rowId,
        ICollection<ValidationIssue> issues
    )
    {
        if (
            value.ValueKind != JsonValueKind.Object
            || value.EnumerateObject().Count() != 3
            || value.EnumerateObject().Any(property => property.Name is not ("sensitiveRef" or "last4" or "kind"))
            || !TryGuid(value, "sensitiveRef")
            || !TryPattern(value, "last4", LastFourPattern())
            || !value.TryGetProperty("kind", out var kindNode)
            || kindNode.ValueKind != JsonValueKind.String
            || !AllowedTaxKinds(component).Contains(kindNode.GetString()!)
        )
            Add(issues, "answer.sensitive_invalid", path, component.Id, rowId, "Sensitive reference is invalid.");
    }

    private static bool TryReadRows(JsonElement value, out IReadOnlyList<RepeaterRow> rows)
    {
        rows = [];
        if (
            value.ValueKind != JsonValueKind.Object
            || value.EnumerateObject().Any(property => property.Name != "rows")
            || !value.TryGetProperty("rows", out var rowsNode)
            || rowsNode.ValueKind != JsonValueKind.Array
        )
            return false;
        var parsed = new List<RepeaterRow>();
        foreach (var row in rowsNode.EnumerateArray())
        {
            if (
                row.ValueKind != JsonValueKind.Object
                || row.EnumerateObject().Any(property => property.Name is not ("rowId" or "values"))
                || !row.TryGetProperty("rowId", out var rowIdNode)
                || rowIdNode.ValueKind != JsonValueKind.String
                || !row.TryGetProperty("values", out var valuesNode)
                || valuesNode.ValueKind != JsonValueKind.Object
            )
                return false;
            parsed.Add(
                new RepeaterRow
                {
                    RowId = rowIdNode.GetString()!,
                    Values = valuesNode
                        .EnumerateObject()
                        .ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal),
                }
            );
        }
        rows = parsed;
        return true;
    }

    private static HashSet<string> AllowedTaxKinds(FormComponent component) =>
        JsonProps.TryGet(component.Props, "kinds", out var kinds) && kinds.ValueKind == JsonValueKind.Array
            ? kinds.EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(["ssn", "itin"], StringComparer.Ordinal);

    private static string? ReadString(JsonElement value, string name) =>
        value.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.String
            ? node.GetString()?.Trim()
            : null;

    private static bool TryGuid(JsonElement value, string name) =>
        value.TryGetProperty(name, out var node)
        && node.ValueKind == JsonValueKind.String
        && Guid.TryParse(node.GetString(), out _);

    private static bool TryPattern(JsonElement value, string name, Regex pattern) =>
        value.TryGetProperty(name, out var node)
        && node.ValueKind == JsonValueKind.String
        && pattern.IsMatch(node.GetString()!);

    private static void TypeMismatch(
        FormComponent component,
        string path,
        string? rowId,
        ICollection<ValidationIssue> issues
    ) => Add(issues, "answer.type_mismatch", path, component.Id, rowId, "Answer type does not match the component.");

    private static void Add(
        ICollection<ValidationIssue> issues,
        string code,
        string path,
        string? componentId,
        string? rowId,
        string message
    ) => issues.Add(new ValidationIssue(code, path, componentId, rowId, message));

    [GeneratedRegex("^rw_[a-z0-9]{10}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex RowIdPattern();

    [GeneratedRegex("^-?[0-9]{1,15}(\\.[0-9]{1,6})?\\z", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    [GeneratedRegex("^-?[0-9]{1,13}(\\.[0-9]{1,2})?\\z", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyPattern();

    [GeneratedRegex("^[0-9]{4}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex LastFourPattern();
}
