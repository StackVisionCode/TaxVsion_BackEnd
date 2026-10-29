using TaxVision.DataCollection.Engine.Components;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Validation;

namespace TaxVision.DataCollection.Engine.Mapping;

internal sealed class MappingValidator(ComponentRegistry registry)
{
    internal ValidationReport Validate(
        FormDefinition definition,
        MappingCatalogSnapshot catalog,
        PublisherCapabilities capabilities
    )
    {
        var context = new MappingValidationContext(
            definition,
            catalog,
            capabilities,
            MappingSources.Create(definition, registry)
        );
        for (var index = 0; index < context.Mappings.Count; index++)
            ValidateMapping(context, context.Mappings[index], index);
        MappingCoverageValidator.Validate(context);
        return new ValidationReport(context.Issues);
    }

    private static void ValidateMapping(MappingValidationContext context, FormMapping mapping, int index)
    {
        var path = $"/mappings/{index}";
        var target = context.Catalog.FindTarget(mapping.TargetKey);
        if (target is null || target.Status == MappingTargetStatus.Retired)
        {
            context.Add(
                IssueCodes.MappingUnknownTarget,
                $"{path}/targetKey",
                mapping.ComponentId,
                "The mapping target does not exist."
            );
            return;
        }
        ValidateTargetUse(context, mapping, target, index, path);
        var section = context.Sources.Section(mapping.ComponentId);
        if (section is not null)
        {
            ValidateCollection(context, mapping, target, section.Value.Section, path);
            return;
        }
        var source = context.Sources.Component(mapping.ComponentId);
        if (source is null)
            context.Add(
                IssueCodes.MappingInvalidTarget,
                $"{path}/componentId",
                mapping.ComponentId,
                "Mapping source was not found."
            );
        else
            ValidateScalar(context, mapping, target, source, path);
    }

    private static void ValidateTargetUse(
        MappingValidationContext context,
        FormMapping mapping,
        MappingTarget target,
        int index,
        string path
    )
    {
        if (target.Status == MappingTargetStatus.Deprecated)
            context.Add(
                IssueCodes.MappingDeprecatedTarget,
                $"{path}/targetKey",
                mapping.ComponentId,
                "The mapping target is deprecated and cannot be used in new versions."
            );
        if (!context.Capabilities.Allows(target.RequiredPermissions, target.RequiresTenantAdmin))
            context.Add(
                IssueCodes.MappingPermissionDenied,
                $"{path}/targetKey",
                mapping.ComponentId,
                "You do not have permission to map this target."
            );
        if (!context.MappedTargets.TryAdd(target.TargetKey, index))
            context.Add(
                IssueCodes.MappingDuplicateTarget,
                $"{path}/targetKey",
                mapping.ComponentId,
                "Only one field can be mapped to this target."
            );
    }

    private static void ValidateScalar(
        MappingValidationContext context,
        FormMapping mapping,
        MappingTarget target,
        MappingSource source,
        string path
    )
    {
        var sourcePath = $"{path}/componentId";
        if (!ValidateSourceShape(context, source, target.DataType, target.Sensitivity, sourcePath))
            return;
        if (!MappingCompatibility.AllowsTransform(target.DataType, mapping.Transform))
            context.Add(
                IssueCodes.MappingTypeMismatch,
                $"{path}/transform",
                source.Component.Id,
                "The transform is not allowed for this target."
            );
        if (mapping.RowFields is not null)
            context.Add(
                IssueCodes.MappingTypeMismatch,
                $"{path}/rowFields",
                source.Component.Id,
                "rowFields are only allowed when mapping a repeater."
            );
        if (target.DataType == MappingTargetType.Enum)
            ValidateOptionMap(context, mapping, target, source, path);
        else if (mapping.OptionMap is not null)
            context.Add(
                IssueCodes.MappingTypeMismatch,
                $"{path}/optionMap",
                source.Component.Id,
                "optionMap is only allowed for choice targets."
            );
        ValidateLimits(context, source.Component, target.MaxLength, target.PastOnly, sourcePath);
    }

    // Falso cuando el origen no puede alimentar al destino; los demás chequeos ya no aportan.
    private static bool ValidateSourceShape(
        MappingValidationContext context,
        MappingSource source,
        MappingTargetType targetType,
        MappingSensitivity sensitivity,
        string path
    )
    {
        var id = source.Component.Id;
        if (source.InRepeater && targetType != MappingTargetType.Collection)
            return Reject(
                context,
                IssueCodes.MappingTypeMismatch,
                path,
                id,
                "Repeater fields are mapped through rowFields."
            );
        if (targetType == MappingTargetType.Collection)
            return Reject(
                context,
                IssueCodes.MappingTypeMismatch,
                path,
                id,
                "Only a repeater section can be mapped here."
            );
        if (source.IsRestricted != (sensitivity == MappingSensitivity.Restricted))
            return Reject(
                context,
                IssueCodes.MappingSensitivityMismatch,
                path,
                id,
                "Tax identifiers map only to restricted targets, and restricted targets only accept tax identifiers."
            );
        if (!MappingCompatibility.IsCompatible(source.Component.Type, targetType))
            return Reject(
                context,
                IssueCodes.MappingTypeMismatch,
                path,
                id,
                $"A '{source.Component.Type}' field cannot be mapped to a {targetType} target."
            );
        return true;
    }

    private static void ValidateOptionMap(
        MappingValidationContext context,
        FormMapping mapping,
        MappingTarget target,
        MappingSource source,
        string path
    )
    {
        var options = JsonProps.OptionValues(source.Component.Props);
        var map = mapping.OptionMap;
        var complete =
            map is not null
            && options.All(map.ContainsKey)
            && map.Keys.All(options.Contains)
            && map.Values.All(value => target.AllowedValues.Contains(value, StringComparer.Ordinal));
        if (!complete)
            context.Add(
                IssueCodes.MappingOptionMapIncomplete,
                $"{path}/optionMap",
                source.Component.Id,
                "Every option must map to a value accepted by the target."
            );
    }

    private static void ValidateLimits(
        MappingValidationContext context,
        FormComponent component,
        int? maxLength,
        bool pastOnly,
        string path
    )
    {
        int? componentMax = component.Type switch
        {
            "text" => JsonProps.GetInt(component.Props, "maxLength", 200),
            "textarea" => JsonProps.GetInt(component.Props, "maxLength", 2_000),
            _ => null,
        };
        if (maxLength is int max && componentMax > max)
            context.Add(
                IssueCodes.MappingLengthExceedsTarget,
                path,
                component.Id,
                $"The field allows {componentMax} characters but the target accepts at most {max}."
            );
        if (pastOnly && component.Type == "date" && JsonProps.GetString(component.Props, "max") != "today")
            context.Add(
                IssueCodes.MappingTypeMismatch,
                path,
                component.Id,
                "This target only accepts past dates: set the field's latest date to today."
            );
    }

    private static void ValidateCollection(
        MappingValidationContext context,
        FormMapping mapping,
        MappingTarget target,
        FormSection section,
        string path
    )
    {
        if (section.Repeat is null || target.DataType != MappingTargetType.Collection)
        {
            context.Add(
                IssueCodes.MappingTypeMismatch,
                $"{path}/componentId",
                mapping.ComponentId,
                "Only a repeater section can be mapped, and only to a collection target."
            );
            return;
        }
        if (mapping.OptionMap is not null || mapping.Transform != MappingTransform.None)
            context.Add(
                IssueCodes.MappingTypeMismatch,
                path,
                mapping.ComponentId,
                "A repeater mapping does not accept optionMap or transform."
            );
        if (mapping.RowFields is null || mapping.RowFields.Count == 0)
        {
            context.Add(
                IssueCodes.MappingTypeMismatch,
                $"{path}/rowFields",
                mapping.ComponentId,
                "A repeater mapping requires rowFields."
            );
            return;
        }
        ValidateRowFields(context, mapping.RowFields, target, section, path);
    }

    private static void ValidateRowFields(
        MappingValidationContext context,
        IReadOnlyDictionary<string, string> rowFields,
        MappingTarget target,
        FormSection section,
        string path
    )
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (componentId, fieldName) in rowFields.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            ValidateRowField(context, target, section, componentId, fieldName, $"{path}/rowFields/{componentId}", used);
        foreach (var field in target.RowFields)
        {
            if (field.Required && !used.Contains(field.Name))
                context.Add(
                    IssueCodes.MappingRequiredTargetMissing,
                    $"{path}/rowFields",
                    section.Id,
                    $"The row field '{field.Name}' must be mapped."
                );
        }
    }

    private static void ValidateRowField(
        MappingValidationContext context,
        MappingTarget target,
        FormSection section,
        string componentId,
        string fieldName,
        string path,
        ISet<string> used
    )
    {
        var source = context.Sources.Component(componentId);
        if (source is null || !ReferenceEquals(source.Section, section))
        {
            context.Add(
                IssueCodes.MappingTypeMismatch,
                path,
                componentId,
                "Row fields must belong to the mapped repeater."
            );
            return;
        }
        var field = target.RowFields.FirstOrDefault(item => item.Name == fieldName);
        if (field is null)
        {
            context.Add(
                IssueCodes.MappingUnknownTarget,
                path,
                componentId,
                "The row field does not exist on the target."
            );
            return;
        }
        if (!used.Add(fieldName))
            context.Add(
                IssueCodes.MappingDuplicateTarget,
                path,
                componentId,
                "Only one field can be mapped to this row field."
            );
        // Una fila nunca alimenta un enum: rowFields no lleva optionMap.
        var fieldType = field.DataType == MappingTargetType.Enum ? MappingTargetType.Collection : field.DataType;
        if (ValidateRowSource(context, source, fieldType, target.Sensitivity, path))
            ValidateLimits(context, source.Component, field.MaxLength, pastOnly: false, path);
    }

    private static bool ValidateRowSource(
        MappingValidationContext context,
        MappingSource source,
        MappingTargetType fieldType,
        MappingSensitivity sensitivity,
        string path
    )
    {
        var id = source.Component.Id;
        if (source.IsRestricted != (sensitivity == MappingSensitivity.Restricted))
            return Reject(
                context,
                IssueCodes.MappingSensitivityMismatch,
                path,
                id,
                "Tax identifiers map only to restricted targets, and restricted targets only accept tax identifiers."
            );
        if (!MappingCompatibility.IsCompatible(source.Component.Type, fieldType))
            return Reject(
                context,
                IssueCodes.MappingTypeMismatch,
                path,
                id,
                $"A '{source.Component.Type}' field cannot be mapped to this row field."
            );
        return true;
    }

    private static bool Reject(MappingValidationContext context, string code, string path, string id, string message)
    {
        context.Add(code, path, id, message);
        return false;
    }
}
