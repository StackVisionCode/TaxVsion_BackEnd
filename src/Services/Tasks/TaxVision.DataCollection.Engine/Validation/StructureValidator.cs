using System.Text.Json;
using System.Text.RegularExpressions;
using TaxVision.DataCollection.Engine.Components;
using TaxVision.DataCollection.Engine.Conditions;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Text;

namespace TaxVision.DataCollection.Engine.Validation;

internal sealed partial class StructureValidator
{
    private readonly DataCollectionLimits _limits;
    private readonly ComponentRegistry _registry;
    private readonly ComponentPropertiesValidator _properties;

    internal StructureValidator(DataCollectionLimits limits, ComponentRegistry registry)
    {
        _limits = limits;
        _registry = registry;
        _properties = new ComponentPropertiesValidator(limits);
    }

    internal ValidationReport Validate(FormDefinition definition, bool publish)
    {
        var issues = new List<ValidationIssue>();
        ValidateRoot(definition, publish, issues);
        if (definition.Pages is null)
            return new ValidationReport(issues);

        var index = DefinitionIndex.Create(definition, _registry);
        ValidateDocument(definition, index, publish, issues);
        return new ValidationReport(issues);
    }

    private void ValidateRoot(FormDefinition definition, bool publish, ICollection<ValidationIssue> issues)
    {
        if (definition.SchemaVersion != 1)
            Add(
                issues,
                "definition.unsupported_schema_version",
                "/schemaVersion",
                null,
                "Only schema version 1 is supported."
            );
        if (
            string.IsNullOrWhiteSpace(definition.Title)
            || definition.Title.Length > 200
            || definition.Title.Any(char.IsControl)
        )
            Add(
                issues,
                "props.out_of_range",
                "/title",
                null,
                "Definition title must contain 1 to 200 plain-text characters."
            );
        foreach (var issue in RichTextValidator.Validate(definition.Description, "/description", 2_000))
            issues.Add(issue);
        if (!publish)
            return;
        if (definition.Pages.Count is < 1)
            Add(issues, "limits.too_many_pages", "/pages", null, "At least one page is required.");
        if (definition.Pages.Count > _limits.MaxPages)
            Add(issues, "limits.too_many_pages", "/pages", null, "Definition has too many pages.");
        if (definition.Presentation == FormPresentation.SinglePage && definition.Pages.Count != 1)
            Add(
                issues,
                "page.single_page_violation",
                "/pages",
                null,
                "Single-page forms must contain exactly one page."
            );
    }

    private void ValidateDocument(
        FormDefinition definition,
        DefinitionIndex index,
        bool publish,
        ICollection<ValidationIssue> issues
    )
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var totalComponents = 0;
        var order = 0;
        var conditionValidator = new ConditionValidator(_limits);
        var signatureSteps = 0;

        for (var pageIndex = 0; pageIndex < definition.Pages.Count; pageIndex++)
        {
            var page = definition.Pages[pageIndex];
            var pagePath = $"/pages/{pageIndex}";
            order++;
            ValidateId(page.Id, PageIdPattern(), pagePath, ids, issues);
            if (publish && definition.Presentation == FormPresentation.Wizard && string.IsNullOrWhiteSpace(page.Title))
                Add(
                    issues,
                    "page.title_required",
                    $"{pagePath}/title",
                    null,
                    "Page title is required in wizard presentation."
                );
            if (page.Title?.Length > 120)
                Add(issues, "props.out_of_range", $"{pagePath}/title", null, "Page title is too long.");
            foreach (var issue in RichTextValidator.Validate(page.Description, $"{pagePath}/description", 2_000))
                issues.Add(issue);
            StyleValidator.Validate(page.Style, $"{pagePath}/style", issues);
            if (publish)
                conditionValidator.Validate(page.VisibleWhen, $"{pagePath}/visibleWhen", order, null, index, issues);

            if (publish && page.Sections.Count is < 1)
                Add(
                    issues,
                    "limits.too_many_sections",
                    $"{pagePath}/sections",
                    null,
                    "At least one section is required."
                );
            if (page.Sections.Count > _limits.MaxSectionsPerPage)
                Add(issues, "limits.too_many_sections", $"{pagePath}/sections", null, "Page has too many sections.");

            for (var sectionIndex = 0; sectionIndex < page.Sections.Count; sectionIndex++)
            {
                var section = page.Sections[sectionIndex];
                var sectionPath = $"{pagePath}/sections/{sectionIndex}";
                order++;
                ValidateSection(section, sectionPath, order, index, ids, conditionValidator, publish, issues);
                totalComponents += section.Components.Count;

                for (var componentIndex = 0; componentIndex < section.Components.Count; componentIndex++)
                {
                    var component = section.Components[componentIndex];
                    var componentPath = $"{sectionPath}/components/{componentIndex}";
                    order++;
                    ValidateComponent(
                        component,
                        componentPath,
                        order,
                        section,
                        index,
                        ids,
                        keys,
                        conditionValidator,
                        publish,
                        issues
                    );
                    if (component.Type == "signatureStep")
                        signatureSteps++;
                }
            }
        }

        if (totalComponents > _limits.MaxComponents)
            Add(issues, "limits.too_many_components", "/pages", null, "Definition has too many components.");
        if (publish)
        {
            ValidateMappings(definition, index, issues);
            ValidateActions(definition, signatureSteps, index, conditionValidator, issues);
        }
    }

    private void ValidateSection(
        FormSection section,
        string path,
        int order,
        DefinitionIndex index,
        ISet<string> ids,
        ConditionValidator conditionValidator,
        bool publish,
        ICollection<ValidationIssue> issues
    )
    {
        ValidateId(section.Id, SectionIdPattern(), path, ids, issues);
        if (section.Title?.Length > 120)
            Add(issues, "props.out_of_range", $"{path}/title", null, "Section title is too long.");
        if (publish && section.Repeat is not null && string.IsNullOrWhiteSpace(section.Title))
            Add(issues, "section.title_required", $"{path}/title", null, "Repeater section title is required.");
        foreach (var issue in RichTextValidator.Validate(section.Description, $"{path}/description", 2_000))
            issues.Add(issue);
        StyleValidator.Validate(section.Style, $"{path}/style", issues);
        if (publish)
            conditionValidator.Validate(section.VisibleWhen, $"{path}/visibleWhen", order, null, index, issues);

        if (section.Repeat is not null)
        {
            var repeat = section.Repeat;
            if (
                repeat.MinRows < 0
                || repeat.MaxRows < 1
                || repeat.MinRows > repeat.MaxRows
                || repeat.MaxRows > _limits.MaxRepeaterRows
            )
                Add(issues, "repeater.invalid_bounds", $"{path}/repeat", null, "Repeater row bounds are invalid.");
            if (
                string.IsNullOrWhiteSpace(repeat.AddLabel)
                || repeat.AddLabel.Length > 60
                || string.IsNullOrWhiteSpace(repeat.RowLabel)
                || repeat.RowLabel.Length > 60
            )
                Add(issues, "repeater.invalid_bounds", $"{path}/repeat", null, "Repeater labels are invalid.");
        }

        if (publish && section.Components.Count is < 1)
            Add(
                issues,
                "limits.too_many_components",
                $"{path}/components",
                null,
                "At least one component is required."
            );
        if (section.Components.Count > _limits.MaxComponentsPerSection)
            Add(issues, "limits.too_many_components", $"{path}/components", null, "Section has too many components.");
    }

    private void ValidateComponent(
        FormComponent component,
        string path,
        int order,
        FormSection section,
        DefinitionIndex index,
        ISet<string> ids,
        ISet<string> keys,
        ConditionValidator conditionValidator,
        bool publish,
        ICollection<ValidationIssue> issues
    )
    {
        ValidateId(component.Id, ComponentIdPattern(), path, ids, issues);
        if (component.Key is not null)
        {
            if (!KeyPattern().IsMatch(component.Key))
                Add(issues, "key.invalid_format", $"{path}/key", component.Id, "Component key format is invalid.");
            else if (!keys.Add(component.Key))
                Add(issues, "key.duplicate", $"{path}/key", component.Id, "Component key must be unique.");
        }
        if (component.Reportable && component.Key is null)
            Add(issues, "key.required", $"{path}/key", component.Id, "Reportable components require a key.");

        if (!_registry.TryGet(component.Type, out var componentType))
        {
            Add(issues, "component.unknown_type", $"{path}/type", component.Id, "Component type is not supported.");
            return;
        }
        if (!publish)
            return;

        _properties.Validate(component, componentType, path, issues);
        conditionValidator.Validate(
            component.VisibleWhen,
            $"{path}/visibleWhen",
            order,
            section.Repeat is null ? null : section.Id,
            index,
            issues
        );
        conditionValidator.Validate(
            component.RequiredWhen,
            $"{path}/requiredWhen",
            order,
            section.Repeat is null ? null : section.Id,
            index,
            issues
        );
    }

    private static void ValidateMappings(
        FormDefinition definition,
        DefinitionIndex index,
        ICollection<ValidationIssue> issues
    )
    {
        var mappedComponents = new HashSet<string>(StringComparer.Ordinal);
        for (var mappingIndex = 0; mappingIndex < definition.Mappings.Count; mappingIndex++)
        {
            var mapping = definition.Mappings[mappingIndex];
            var path = $"/mappings/{mappingIndex}";
            if (!index.Components.ContainsKey(mapping.ComponentId) && !index.Sections.ContainsKey(mapping.ComponentId))
                Add(
                    issues,
                    "mapping.invalid_target",
                    $"{path}/componentId",
                    mapping.ComponentId,
                    "Mapping source was not found."
                );
            if (string.IsNullOrWhiteSpace(mapping.TargetKey))
                Add(
                    issues,
                    "mapping.invalid_target",
                    $"{path}/targetKey",
                    mapping.ComponentId,
                    "Mapping target is required."
                );
            if (!mappedComponents.Add(mapping.ComponentId))
                Add(
                    issues,
                    "mapping.duplicate_target",
                    path,
                    mapping.ComponentId,
                    "A component can be mapped only once in v1."
                );
        }
    }

    private static void ValidateActions(
        FormDefinition definition,
        int signatureSteps,
        DefinitionIndex index,
        ConditionValidator conditionValidator,
        ICollection<ValidationIssue> issues
    )
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var hasSignatureAction = false;
        for (var actionIndex = 0; actionIndex < definition.Actions.Count; actionIndex++)
        {
            var action = definition.Actions[actionIndex];
            var path = $"/actions/{actionIndex}";
            if (!KeyPattern().IsMatch(action.Key) || !keys.Add(action.Key))
                Add(issues, "key.invalid_format", $"{path}/key", null, "Action key must be valid and unique.");
            if (!TriggerAllowed(action.Type, action.Trigger))
                Add(issues, "props.out_of_range", $"{path}/trigger", null, "Action trigger is not supported.");
            if (action.Type is FormActionType.NotifyStaff or FormActionType.RouteToReview)
                Add(
                    issues,
                    "props.out_of_range",
                    $"{path}/type",
                    null,
                    "This action is implicit and cannot be configured."
                );
            ValidateActionConfig(action, path, issues);
            conditionValidator.Validate(action.Condition, $"{path}/condition", int.MaxValue, null, index, issues);
            hasSignatureAction |= action.Type == FormActionType.RequestSignature;
        }
        if (signatureSteps > 1)
            Add(issues, "props.out_of_range", "/pages", null, "Only one signatureStep is allowed.");
        if (signatureSteps == 1 && !hasSignatureAction)
            Add(
                issues,
                "signature.action_missing",
                "/actions",
                null,
                "signatureStep requires a RequestSignature action."
            );
        if (signatureSteps == 0 && hasSignatureAction)
            Add(
                issues,
                "signature.action_missing",
                "/actions",
                null,
                "RequestSignature requires a signatureStep component."
            );
    }

    private static void ValidateActionConfig(FormAction action, string path, ICollection<ValidationIssue> issues)
    {
        if (action.Config.ValueKind == JsonValueKind.Undefined)
            return;
        if (action.Config.ValueKind != JsonValueKind.Object)
        {
            Add(issues, "props.out_of_range", $"{path}/config", null, "Action config must be an object.");
            return;
        }

        if (action.Type != FormActionType.CreateCustomer)
        {
            if (action.Config.EnumerateObject().Any())
                Add(
                    issues,
                    "props.unknown",
                    $"{path}/config",
                    null,
                    "This action does not accept config in schema v1."
                );
            return;
        }

        var allowed = new HashSet<string>(["kind", "language", "postCreate", "waitFor"], StringComparer.Ordinal);
        foreach (var property in action.Config.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
                Add(
                    issues,
                    "props.unknown",
                    $"{path}/config/{property.Name}",
                    null,
                    "Action config property is not supported."
                );
        }

        foreach (var name in new[] { "kind", "language" })
        {
            if (action.Config.TryGetProperty(name, out var source) && !IsActionValueSource(source))
                Add(issues, "props.out_of_range", $"{path}/config/{name}", null, "Action value source is invalid.");
        }

        if (
            action.Config.TryGetProperty("waitFor", out var waitFor)
            && (
                waitFor.ValueKind != JsonValueKind.String || waitFor.GetString() is not ("none" or "signatureCompleted")
            )
        )
            Add(issues, "props.out_of_range", $"{path}/config/waitFor", null, "Action waitFor value is invalid.");

        if (
            action.Config.TryGetProperty("postCreate", out var postCreate)
            && (
                postCreate.ValueKind != JsonValueKind.Array
                || postCreate
                    .EnumerateArray()
                    .Any(item => item.ValueKind != JsonValueKind.String || item.GetString() != "primaryAddress")
                || postCreate.EnumerateArray().Select(item => item.GetString()).Distinct(StringComparer.Ordinal).Count()
                    != postCreate.GetArrayLength()
            )
        )
            Add(issues, "props.out_of_range", $"{path}/config/postCreate", null, "Action postCreate value is invalid.");
    }

    private static bool IsActionValueSource(JsonElement source)
    {
        if (source.ValueKind != JsonValueKind.Object || source.EnumerateObject().Count() != 1)
            return false;
        if (source.TryGetProperty("constant", out var constant))
            return constant.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(constant.GetString());
        return source.TryGetProperty("mapped", out var mapped) && mapped.ValueKind == JsonValueKind.True;
    }

    private static bool TriggerAllowed(FormActionType type, FormActionTrigger trigger) =>
        type switch
        {
            FormActionType.NotifyStaff or FormActionType.RouteToReview => trigger == FormActionTrigger.OnSubmit,
            FormActionType.GenerateSummary => trigger is FormActionTrigger.OnSubmit or FormActionTrigger.OnComplete,
            FormActionType.CreateCustomer => trigger is FormActionTrigger.OnSubmit or FormActionTrigger.Manual,
            FormActionType.UpdateCustomerProfile => trigger == FormActionTrigger.Manual,
            FormActionType.RequestSignature => trigger is FormActionTrigger.OnSubmit or FormActionTrigger.OnComplete,
            FormActionType.ApplyRetentionClass => trigger == FormActionTrigger.OnComplete,
            _ => false,
        };

    private static void ValidateId(
        string id,
        Regex pattern,
        string path,
        ISet<string> ids,
        ICollection<ValidationIssue> issues
    )
    {
        if (id is null || !pattern.IsMatch(id))
            Add(issues, "id.invalid_format", $"{path}/id", id, "Identifier format is invalid.");
        else if (!ids.Add(id))
            Add(issues, "id.duplicate", $"{path}/id", id, "Identifier must be unique.");
    }

    private static void Add(
        ICollection<ValidationIssue> issues,
        string code,
        string path,
        string? componentId,
        string message
    ) => issues.Add(new ValidationIssue(code, path, componentId, null, message));

    [GeneratedRegex("^pg_[a-z0-9]{8,16}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex PageIdPattern();

    [GeneratedRegex("^sc_[a-z0-9]{8,16}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SectionIdPattern();

    [GeneratedRegex("^fd_[a-z0-9]{8,16}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex ComponentIdPattern();

    [GeneratedRegex("^[a-z][a-z0-9_]{1,63}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
