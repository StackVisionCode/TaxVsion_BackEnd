using System.Text.Json;
using System.Text.RegularExpressions;
using TaxVision.DataCollection.Engine.Components;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Formats;
using TaxVision.DataCollection.Engine.Text;

namespace TaxVision.DataCollection.Engine.Validation;

internal sealed partial class ComponentPropertiesValidator(DataCollectionLimits limits)
{
    private static readonly HashSet<string> FileTypes = new(
        ["pdf", "jpg", "jpeg", "png", "docx", "xlsx", "csv", "txt"],
        StringComparer.Ordinal
    );
    private static readonly HashSet<string> AutocompleteValues = new(
        [
            "name",
            "given-name",
            "family-name",
            "organization",
            "street-address",
            "address-line1",
            "address-line2",
            "address-level2",
            "address-level1",
            "postal-code",
            "bday",
            "email",
            "tel",
            "off",
        ],
        StringComparer.Ordinal
    );

    // PrefillSource cerrado (P-13.1).
    private static readonly HashSet<string> PrefillSources = new(
        ["previousRequest", "customer.displayName"],
        StringComparer.Ordinal
    );

    [GeneratedRegex("(?i)\\b(ssn|social security|itin|ein|tax id)\\b", RegexOptions.CultureInvariant)]
    private static partial Regex TaxIdWords();

    [GeneratedRegex(
        "(?i)\\b(bank account|routing number|card number|cvv|pin|password)\\b",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex UnsupportedWords();

    internal void Validate(
        FormComponent component,
        IComponentType componentType,
        string path,
        ICollection<ValidationIssue> issues
    )
    {
        ValidateCommon(component, componentType, path, issues);
        if (!JsonProps.IsObject(component.Props))
        {
            Add(issues, "props.required", $"{path}/props", component.Id, "Props must be an object.");
            return;
        }

        foreach (var property in JsonProps.Enumerate(component.Props))
        {
            if (!componentType.AllowedProperties.Contains(property.Name))
                Add(
                    issues,
                    "props.unknown",
                    $"{path}/props/{property.Name}",
                    component.Id,
                    "Property is not supported by this component type."
                );
        }
        if (!ValidatePropertyTypes(component, path, issues))
            return;

        switch (component.Type)
        {
            case "heading":
                ValidateHeading(component, path, issues);
                break;
            case "paragraph":
                ValidateRequiredRichText(component, "text", limits.MaxParagraphLength, path, issues);
                break;
            case "callout":
                ValidateCallout(component, path, issues);
                break;
            case "spacer":
                ValidateEnum(component, "size", ["2", "4", "6", "8"], path, issues, required: true);
                break;
            case "consent":
                ValidateConsent(component, path, issues);
                break;
            case "signatureStep":
                ValidateSignatureStep(component, path, issues);
                break;
            case "text":
                ValidateText(component, path, issues);
                break;
            case "textarea":
                ValidateTextarea(component, path, issues);
                break;
            case "number":
                ValidateNumber(component, path, issues);
                break;
            case "currency":
                ValidateCurrency(component, path, issues);
                break;
            case "date":
                ValidateDate(component, path, issues);
                break;
            case "phone":
                ValidateEnum(component, "defaultCountry", ["US"], path, issues, defaultValue: "US");
                break;
            case "select":
                ValidateChoice(component, path, issues, 1);
                if (JsonProps.TryGet(component.Props, "placeholder", out _))
                    ValidatePlainText(component, "placeholder", 0, 60, path, issues);
                break;
            case "radio":
                ValidateChoice(component, path, issues, 2);
                ValidateEnum(component, "layout", ["vertical", "horizontal"], path, issues, defaultValue: "vertical");
                break;
            case "checkboxGroup":
                ValidateCheckboxGroup(component, path, issues);
                break;
            case "yesNo":
                ValidateYesNo(component, path, issues);
                break;
            case "checkbox":
                ValidateRequiredRichText(component, "text", 500, path, issues);
                break;
            case "file":
                ValidateFile(component, path, issues);
                break;
            case "address":
                ValidateEnum(component, "country", ["US"], path, issues, defaultValue: "US");
                break;
            case "taxId":
                ValidateTaxId(component, path, issues);
                break;
        }
    }

    private static bool ValidatePropertyTypes(FormComponent component, string path, ICollection<ValidationIssue> issues)
    {
        var valid = true;
        foreach (var property in JsonProps.Enumerate(component.Props))
        {
            var expected = ExpectedKinds(component.Type, property.Name);
            if (expected.Count > 0 && !expected.Contains(property.Value.ValueKind))
            {
                Add(
                    issues,
                    "props.out_of_range",
                    $"{path}/props/{property.Name}",
                    component.Id,
                    "Property has an invalid JSON type."
                );
                valid = false;
            }
            else if (!HasValidNumber(component.Type, property))
            {
                // Sin esto, 2.5 o 1e12 caerían en silencio al valor por defecto.
                Add(
                    issues,
                    "props.out_of_range",
                    $"{path}/props/{property.Name}",
                    component.Id,
                    "Property is not a supported number."
                );
                valid = false;
            }
        }
        return valid;
    }

    private static bool HasValidNumber(string type, JsonProperty property) =>
        (type, property.Name) switch
        {
            ("currency" or "number", "min" or "max") => property.Value.TryGetDecimal(out _),
            ("file", "maxFileBytes") => PlainNumberFormat.TryReadInteger(
                property.Value,
                long.MinValue,
                long.MaxValue,
                out _
            ),
            _ when property.Value.ValueKind == JsonValueKind.Number => PlainNumberFormat.TryReadInteger(
                property.Value,
                int.MinValue,
                int.MaxValue,
                out _
            ),
            _ => true,
        };

    private static IReadOnlySet<JsonValueKind> ExpectedKinds(string type, string name) =>
        (type, name) switch
        {

            ("heading", "level")
            or
            ("consent", "noticeVersionNumber")
            or
            ("text", "maxLength" or "minLength")
            or
            ("textarea", "maxLength" or "rows")
            or
            ("number", "decimals")
            or
            ("checkboxGroup", "minSelected" or "maxSelected")
            or ("file", "maxFiles" or "maxFileBytes") => NumberKinds,

            ("currency", "min" or "max") or ("number", "min" or "max") => NumberKinds,

            ("consent", "blocking") or ("currency", "allowNegative") => BooleanKinds,


            ("paragraph", "text")
            or
            ("callout", "text")
            or
            ("checkbox", "text")
            or
            ("signatureStep", "instructions")
            or ("file", "instructions") => RichTextKinds,


            ("select", "options")
            or
            ("radio", "options")
            or
            ("checkboxGroup", "options")
            or
            ("file", "allowedTypes")
            or ("taxId", "kinds") => ArrayKinds,

            _ => StringKinds,
        };

    private static readonly IReadOnlySet<JsonValueKind> StringKinds = new HashSet<JsonValueKind>
    {
        JsonValueKind.String,
    };
    private static readonly IReadOnlySet<JsonValueKind> NumberKinds = new HashSet<JsonValueKind>
    {
        JsonValueKind.Number,
    };
    private static readonly IReadOnlySet<JsonValueKind> BooleanKinds = new HashSet<JsonValueKind>
    {
        JsonValueKind.True,
        JsonValueKind.False,
    };
    private static readonly IReadOnlySet<JsonValueKind> RichTextKinds = new HashSet<JsonValueKind>
    {
        JsonValueKind.Object,
        JsonValueKind.Null,
    };
    private static readonly IReadOnlySet<JsonValueKind> ArrayKinds = new HashSet<JsonValueKind> { JsonValueKind.Array };

    private void ValidateCommon(
        FormComponent component,
        IComponentType componentType,
        string path,
        ICollection<ValidationIssue> issues
    )
    {
        if (componentType.Kind == ComponentKind.Input)
        {
            if (string.IsNullOrWhiteSpace(component.Label))
                Add(issues, "component.label_required", $"{path}/label", component.Id, "Input label is required.");
            else if (component.Label.Length > limits.MaxLabelLength)
                Add(issues, "props.out_of_range", $"{path}/label", component.Id, "Input label is too long.");

            var declared = component.DataClass ?? componentType.MinimumDataClass!.Value;
            if (declared < componentType.MinimumDataClass)
                Add(
                    issues,
                    "dataclass.below_minimum",
                    $"{path}/dataClass",
                    component.Id,
                    "Data class is below the platform minimum."
                );
            if (component.Type == "taxId" && declared != DataClass.Restricted)
                Add(
                    issues,
                    "dataclass.below_minimum",
                    $"{path}/dataClass",
                    component.Id,
                    "Tax identifiers are always restricted."
                );
            if (component.Required && component.RequiredWhen is not null)
                Add(
                    issues,
                    "required.conflict",
                    path,
                    component.Id,
                    "Required and requiredWhen are mutually exclusive."
                );
            if (
                component.Reportable
                && (declared is DataClass.Personal or DataClass.Restricted || component.Type is "taxId" or "file")
            )
                Add(
                    issues,
                    "reportable.not_allowed",
                    $"{path}/reportable",
                    component.Id,
                    "This component cannot be reportable."
                );
            if (component.Prefill is not null && component.Type is "taxId" or "file")
                Add(
                    issues,
                    "prefill.not_allowed",
                    $"{path}/prefill",
                    component.Id,
                    "Prefill is not allowed for this component."
                );
            else if (component.Prefill is not null && !PrefillSources.Contains(component.Prefill.Source))
                Add(
                    issues,
                    "prefill.invalid_source",
                    $"{path}/prefill/source",
                    component.Id,
                    "Prefill source is not supported."
                );
        }
        else
        {
            if (component.Label is not null)
                Add(
                    issues,
                    "component.label_not_allowed",
                    $"{path}/label",
                    component.Id,
                    "Label is not allowed for this component."
                );
            if (
                component.Key is not null
                || component.Required
                || component.RequiredWhen is not null
                || component.Reportable
                || component.Prefill is not null
                || component.DataClass is not null
            )
                Add(
                    issues,
                    "props.unknown",
                    path,
                    component.Id,
                    "Input-only properties are not allowed for this component."
                );
        }

        var staffText = $"{component.Label} {RichTextValidator.PlainText(component.Help)}";
        if (TaxIdWords().IsMatch(staffText) && component.Type != "taxId")
            Add(
                issues,
                "component.restricted_requires_taxid",
                path,
                component.Id,
                "Tax identifiers require the dedicated taxId component."
            );
        if (UnsupportedWords().IsMatch(staffText))
            Add(
                issues,
                "component.unsupported_restricted_data",
                path,
                component.Id,
                "This restricted data cannot be collected in forms."
            );

        foreach (var issue in RichTextValidator.Validate(component.Help, $"{path}/help", limits.MaxHelpTextLength))
            issues.Add(issue with { ComponentId = component.Id });
        StyleValidator.Validate(component.Style, $"{path}/style", issues);
    }

    private static void ValidateHeading(FormComponent component, string path, ICollection<ValidationIssue> issues)
    {
        ValidatePlainText(component, "text", 1, 200, path, issues);
        var level = JsonProps.GetInt(component.Props, "level", 2);
        if (level is not (2 or 3))
            Add(issues, "props.out_of_range", $"{path}/props/level", component.Id, "Heading level must be 2 or 3.");
    }

    private void ValidateCallout(FormComponent component, string path, ICollection<ValidationIssue> issues)
    {
        ValidateEnum(component, "tone", ["info", "warning", "success"], path, issues, defaultValue: "info");
        if (JsonProps.TryGet(component.Props, "title", out _))
            ValidatePlainText(component, "title", 0, 120, path, issues);
        ValidateRequiredRichText(component, "text", 2_000, path, issues);
    }

    private static void ValidateConsent(FormComponent component, string path, ICollection<ValidationIssue> issues)
    {
        ValidatePlainText(component, "noticeKey", 1, 120, path, issues);
        if (JsonProps.GetInt(component.Props, "noticeVersionNumber", 0) < 1)
            Add(
                issues,
                "props.out_of_range",
                $"{path}/props/noticeVersionNumber",
                component.Id,
                "Published notice version is required."
            );
        ValidateEnum(component, "mode", ["noticeOnly", "acceptance"], path, issues, required: true);
        var mode = JsonProps.GetString(component.Props, "mode");
        var timing = JsonProps.GetString(component.Props, "timing");
        var blocking = JsonProps.GetBool(component.Props, "blocking", false);
        if (mode == "acceptance")
        {
            if (timing is not ("beforeCollection" or "beforeSubmit"))
                Add(issues, "props.required", $"{path}/props/timing", component.Id, "Acceptance timing is required.");
            ValidatePlainText(component, "acceptanceText", 1, 300, path, issues);
        }
        else if (timing is not null)
        {
            Add(
                issues,
                "props.unknown",
                $"{path}/props/timing",
                component.Id,
                "Timing applies only to acceptance consent."
            );
        }
        if (mode != "acceptance" && blocking)
            Add(
                issues,
                "props.unknown",
                $"{path}/props/blocking",
                component.Id,
                "Only acceptance consent can block progress."
            );
    }

    private static void ValidateSignatureStep(FormComponent component, string path, ICollection<ValidationIssue> issues)
    {
        ValidateEnum(component, "documentSource", ["submissionSummary"], path, issues, required: true);
        ValidateEnum(component, "verification", ["emailOtp", "smsOtp"], path, issues, required: true);
        ValidateOptionalRichText(component, "instructions", 1_000, path, issues);
    }

    private static void ValidateText(FormComponent component, string path, ICollection<ValidationIssue> issues)
    {
        var max = JsonProps.GetInt(component.Props, "maxLength", 200);
        var min = JsonProps.GetInt(component.Props, "minLength", 0);
        if (max is < 1 or > 1_000 || min < 0 || min > max)
            Add(issues, "props.out_of_range", $"{path}/props", component.Id, "Text length bounds are invalid.");
        var autocomplete = JsonProps.GetString(component.Props, "autocomplete");
        if (autocomplete is not null && !AutocompleteValues.Contains(autocomplete))
            Add(
                issues,
                "props.out_of_range",
                $"{path}/props/autocomplete",
                component.Id,
                "Autocomplete value is invalid."
            );
    }

    private static void ValidateTextarea(FormComponent component, string path, ICollection<ValidationIssue> issues)
    {
        var max = JsonProps.GetInt(component.Props, "maxLength", 2_000);
        var rows = JsonProps.GetInt(component.Props, "rows", 4);
        if (max is < 1 or > 10_000 || rows is < 2 or > 12)
            Add(
                issues,
                "props.out_of_range",
                $"{path}/props",
                component.Id,
                "Textarea properties are outside the allowed range."
            );
    }

    private static void ValidateNumber(FormComponent component, string path, ICollection<ValidationIssue> issues)
    {
        var decimals = JsonProps.GetInt(component.Props, "decimals", 0);
        var min = JsonProps.GetDecimal(component.Props, "min");
        var max = JsonProps.GetDecimal(component.Props, "max");
        if (decimals is < 0 or > 6 || (min.HasValue && max.HasValue && min > max))
            Add(
                issues,
                "props.out_of_range",
                $"{path}/props",
                component.Id,
                "Number properties are outside the allowed range."
            );
        var unit = JsonProps.GetString(component.Props, "unitLabel");
        if (unit?.Length > 20)
            Add(issues, "props.out_of_range", $"{path}/props/unitLabel", component.Id, "Unit label is too long.");
    }

    private static void ValidateCurrency(FormComponent component, string path, ICollection<ValidationIssue> issues)
    {
        ValidateEnum(component, "currency", ["USD"], path, issues, defaultValue: "USD");
        var min = JsonProps.GetDecimal(component.Props, "min") ?? 0;
        var max = JsonProps.GetDecimal(component.Props, "max");
        if (max.HasValue && min > max)
            Add(issues, "props.out_of_range", $"{path}/props", component.Id, "Currency bounds are invalid.");
    }

    private static void ValidateDate(FormComponent component, string path, ICollection<ValidationIssue> issues)
    {
        foreach (var name in new[] { "min", "max" })
        {
            var value = JsonProps.GetString(component.Props, name);
            if (value is not null && value != "today" && !DateFormat.TryParse(value, out _))
                Add(issues, "props.out_of_range", $"{path}/props/{name}", component.Id, "Date bound is invalid.");
        }
    }

    private void ValidateChoice(FormComponent component, string path, ICollection<ValidationIssue> issues, int minimum)
    {
        if (!JsonProps.TryGet(component.Props, "options", out var options) || options.ValueKind != JsonValueKind.Array)
        {
            Add(issues, "props.required", $"{path}/props/options", component.Id, "Options are required.");
            return;
        }
        if (options.GetArrayLength() < minimum)
            Add(issues, "options.too_few", $"{path}/props/options", component.Id, "Not enough options were provided.");
        if (options.GetArrayLength() > limits.MaxOptionsPerChoice)
            Add(issues, "options.too_many", $"{path}/props/options", component.Id, "Too many options were provided.");

        var values = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var option in options.EnumerateArray())
        {
            var optionPath = $"{path}/props/options/{index++}";
            if (
                option.ValueKind != JsonValueKind.Object
                || !option.TryGetProperty("value", out var valueNode)
                || !option.TryGetProperty("label", out var labelNode)
            )
            {
                Add(issues, "props.required", optionPath, component.Id, "Option value and label are required.");
                continue;
            }
            var value = valueNode.ValueKind == JsonValueKind.String ? valueNode.GetString() : null;
            var label = labelNode.ValueKind == JsonValueKind.String ? labelNode.GetString() : null;
            if (value is null || !OptionValuePattern().IsMatch(value))
                Add(issues, "props.out_of_range", $"{optionPath}/value", component.Id, "Option value is invalid.");
            else if (!values.Add(value))
                Add(
                    issues,
                    "options.duplicate_value",
                    $"{optionPath}/value",
                    component.Id,
                    "Option values must be unique."
                );
            if (string.IsNullOrWhiteSpace(label) || label.Length > 200)
                Add(issues, "props.out_of_range", $"{optionPath}/label", component.Id, "Option label is invalid.");
            if (option.EnumerateObject().Any(property => property.Name is not ("value" or "label")))
                Add(issues, "props.unknown", optionPath, component.Id, "Option contains an unknown property.");
        }
    }

    private void ValidateCheckboxGroup(FormComponent component, string path, ICollection<ValidationIssue> issues)
    {
        ValidateChoice(component, path, issues, 1);
        var optionCount =
            JsonProps.TryGet(component.Props, "options", out var options) && options.ValueKind == JsonValueKind.Array
                ? options.GetArrayLength()
                : 0;
        var min = JsonProps.GetInt(component.Props, "minSelected", 0);
        var max = JsonProps.GetInt(component.Props, "maxSelected", optionCount);
        if (min < 0 || max < min || max > optionCount)
            Add(issues, "props.out_of_range", $"{path}/props", component.Id, "Selection bounds are invalid.");
    }

    private static void ValidateYesNo(FormComponent component, string path, ICollection<ValidationIssue> issues)
    {
        foreach (var name in new[] { "yesLabel", "noLabel" })
        {
            var value = JsonProps.GetString(component.Props, name);
            if (value is not null && (value.Length < 1 || value.Length > 30))
                Add(issues, "props.out_of_range", $"{path}/props/{name}", component.Id, "Boolean label is invalid.");
        }
    }

    private void ValidateFile(FormComponent component, string path, ICollection<ValidationIssue> issues)
    {
        var maxFiles = JsonProps.GetInt(component.Props, "maxFiles", 5);
        var maxBytes = JsonProps.GetLong(component.Props, "maxFileBytes", 10_485_760);
        if (maxFiles is < 1 || maxFiles > limits.MaxFilesPerField)
            Add(
                issues,
                "props.out_of_range",
                $"{path}/props/maxFiles",
                component.Id,
                "File count is outside the platform limit."
            );
        if (maxBytes < 1 || maxBytes > limits.MaxFileBytes)
            Add(
                issues,
                "file.size_exceeds_platform",
                $"{path}/props/maxFileBytes",
                component.Id,
                "File size exceeds the platform limit."
            );

        if (JsonProps.TryGet(component.Props, "allowedTypes", out var types))
        {
            if (
                types.ValueKind != JsonValueKind.Array
                || types.GetArrayLength() == 0
                || types
                    .EnumerateArray()
                    .Any(item => item.ValueKind != JsonValueKind.String || !FileTypes.Contains(item.GetString()!))
            )
                Add(
                    issues,
                    "file.type_not_allowed",
                    $"{path}/props/allowedTypes",
                    component.Id,
                    "One or more file types are not allowed."
                );
        }
        ValidateOptionalRichText(component, "instructions", 1_000, path, issues);
    }

    private static void ValidateTaxId(FormComponent component, string path, ICollection<ValidationIssue> issues)
    {
        if (!JsonProps.TryGet(component.Props, "kinds", out var kinds))
            return;
        var allowed = new HashSet<string>(["ssn", "itin", "ein"], StringComparer.Ordinal);
        if (
            kinds.ValueKind != JsonValueKind.Array
            || kinds.GetArrayLength() == 0
            || kinds
                .EnumerateArray()
                .Any(item => item.ValueKind != JsonValueKind.String || !allowed.Contains(item.GetString()!))
        )
            Add(issues, "props.out_of_range", $"{path}/props/kinds", component.Id, "Tax identifier kinds are invalid.");
    }

    private static void ValidateRequiredRichText(
        FormComponent component,
        string name,
        int max,
        string path,
        ICollection<ValidationIssue> issues
    )
    {
        if (!JsonProps.TryGet(component.Props, name, out var value))
        {
            Add(issues, "props.required", $"{path}/props/{name}", component.Id, "Rich text is required.");
            return;
        }
        var text = JsonProps.GetRichText(component.Props, name);
        if (value.ValueKind != JsonValueKind.Null && text is null)
        {
            Add(issues, "richtext.invalid_node", $"{path}/props/{name}", component.Id, "Rich text is invalid.");
            return;
        }
        foreach (var issue in RichTextValidator.Validate(text, $"{path}/props/{name}", max))
            issues.Add(issue with { ComponentId = component.Id });
        if (RichTextValidator.CharacterCount(text) == 0)
            Add(issues, "props.required", $"{path}/props/{name}", component.Id, "Rich text cannot be empty.");
    }

    private static void ValidateOptionalRichText(
        FormComponent component,
        string name,
        int max,
        string path,
        ICollection<ValidationIssue> issues
    )
    {
        if (!JsonProps.TryGet(component.Props, name, out var value))
            return;
        var text = JsonProps.GetRichText(component.Props, name);
        if (value.ValueKind != JsonValueKind.Null && text is null)
            Add(issues, "richtext.invalid_node", $"{path}/props/{name}", component.Id, "Rich text is invalid.");
        foreach (var issue in RichTextValidator.Validate(text, $"{path}/props/{name}", max))
            issues.Add(issue with { ComponentId = component.Id });
    }

    private static void ValidatePlainText(
        FormComponent component,
        string name,
        int min,
        int max,
        string path,
        ICollection<ValidationIssue> issues
    )
    {
        var value = JsonProps.GetString(component.Props, name);
        if (value is null || value.Length < min || value.Length > max || value.Any(char.IsControl))
            Add(
                issues,
                "props.out_of_range",
                $"{path}/props/{name}",
                component.Id,
                "Text property is outside the allowed range."
            );
    }

    private static void ValidateEnum(
        FormComponent component,
        string name,
        IReadOnlyCollection<string> allowed,
        string path,
        ICollection<ValidationIssue> issues,
        bool required = false,
        string? defaultValue = null
    )
    {
        var value = JsonProps.GetString(component.Props, name) ?? defaultValue;
        if (value is null && required)
            Add(issues, "props.required", $"{path}/props/{name}", component.Id, "Property is required.");
        else if (value is not null && !allowed.Contains(value))
            Add(issues, "props.out_of_range", $"{path}/props/{name}", component.Id, "Property value is not supported.");
    }

    [GeneratedRegex("^[a-z0-9_]{1,64}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex OptionValuePattern();

    private static void Add(
        ICollection<ValidationIssue> issues,
        string code,
        string path,
        string? componentId,
        string message
    ) => issues.Add(new ValidationIssue(code, path, componentId, null, message));
}
