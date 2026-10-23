using System.Text.Json;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Validation;

namespace TaxVision.DataCollection.Engine.Mapping;

// Cobertura por acción: permisos, constantes de la config, requeridos por operación y por cada Kind posible.
internal static class MappingCoverageValidator
{
    internal static void Validate(MappingValidationContext context)
    {
        var flaggedSources = new HashSet<int>();
        var actions = context.Definition.Actions ?? [];
        for (var index = 0; index < actions.Count; index++)
        {
            var operation = context.Catalog.FindOperation(actions[index].Type.ToString());
            if (operation is not null)
                ValidateAction(context, actions[index], operation, $"/actions/{index}", flaggedSources);
        }
    }

    private static void ValidateAction(
        MappingValidationContext context,
        FormAction action,
        MappingOperation operation,
        string path,
        ISet<int> flaggedSources
    )
    {
        if (!context.Capabilities.Allows(operation.RequiredPermissions, operation.RequiresTenantAdmin))
            context.Add(
                IssueCodes.MappingPermissionDenied,
                $"{path}/type",
                null,
                "You do not have permission to configure this action."
            );
        var constants = ReadConstants(context, action, operation, path);
        var scope = ResolveKinds(context, operation, constants);
        ValidateRequiredTargets(context, operation, constants, scope.Kinds, path);
        ValidateSources(context, operation, scope, flaggedSources);
    }

    private static Dictionary<string, string> ReadConstants(
        MappingValidationContext context,
        FormAction action,
        MappingOperation operation,
        string path
    )
    {
        var constants = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (
            var (configKey, targetKey) in operation.ConfigTargets.OrderBy(pair => pair.Key, StringComparer.Ordinal)
        )
        {
            var target = context.Catalog.FindTarget(targetKey);
            if (target is null || !TryReadConstant(action.Config, configKey, out var value))
                continue;
            ValidateConstant(context, target, value, $"{path}/config/{configKey}");
            constants[targetKey] = value;
        }
        return constants;
    }

    private static void ValidateConstant(
        MappingValidationContext context,
        MappingTarget target,
        string value,
        string path
    )
    {
        if (target.DataType == MappingTargetType.Enum && !target.AllowedValues.Contains(value, StringComparer.Ordinal))
            context.Add(
                IssueCodes.MappingTypeMismatch,
                path,
                null,
                "The constant is not a value accepted by the target."
            );
        if (!context.Capabilities.Allows(target.RequiredPermissions, target.RequiresTenantAdmin))
            context.Add(
                IssueCodes.MappingPermissionDenied,
                path,
                null,
                "You do not have permission to set this target."
            );
        if (context.MappedTargets.ContainsKey(target.TargetKey))
            context.Add(
                IssueCodes.MappingDuplicateTarget,
                path,
                null,
                "This target is already mapped to a field; remove the constant or the mapping."
            );
    }

    private static bool TryReadConstant(JsonElement config, string key, out string value)
    {
        value = string.Empty;
        if (
            config.ValueKind != JsonValueKind.Object
            || !config.TryGetProperty(key, out var source)
            || source.ValueKind != JsonValueKind.Object
            || !source.TryGetProperty("constant", out var constant)
            || constant.ValueKind != JsonValueKind.String
        )
            return false;
        value = constant.GetString()!;
        return true;
    }

    // Constante: un solo Kind. Mapeado: los valores que alcanza su optionMap. Sin ninguno: todos.
    private static KindScope ResolveKinds(
        MappingValidationContext context,
        MappingOperation operation,
        IReadOnlyDictionary<string, string> constants
    )
    {
        var target = context.Catalog.FindTarget(operation.DiscriminatorTargetKey);
        if (target is null)
            return KindScope.Empty;
        if (constants.TryGetValue(target.TargetKey, out var constant))
            return new KindScope(
                new HashSet<string>([constant], StringComparer.Ordinal),
                new KindConditionAnalyzer(null, null)
            );
        var mapping = context.MappingFor(target.TargetKey);
        var optionKinds = (mapping?.OptionMap ?? new Dictionary<string, string>())
            .Where(pair => target.AllowedValues.Contains(pair.Value, StringComparer.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        IEnumerable<string> kinds = optionKinds.Count > 0 ? optionKinds.Values : target.AllowedValues;
        return new KindScope(
            new HashSet<string>(kinds, StringComparer.Ordinal),
            new KindConditionAnalyzer(mapping?.ComponentId, optionKinds)
        );
    }

    private static void ValidateRequiredTargets(
        MappingValidationContext context,
        MappingOperation operation,
        IReadOnlyDictionary<string, string> constants,
        IReadOnlySet<string> kinds,
        string path
    )
    {
        foreach (var target in context.Catalog.Targets)
        {
            if (
                target.Status == MappingTargetStatus.Retired
                || context.MappedTargets.ContainsKey(target.TargetKey)
                || constants.ContainsKey(target.TargetKey)
            )
                continue;
            var missing = target.RequiredFor.FirstOrDefault(requirement =>
                requirement.Operation == operation.Name
                && (requirement.Kind is null || kinds.Contains(requirement.Kind))
            );
            if (missing is not null)
                context.Add(
                    IssueCodes.MappingRequiredTargetMissing,
                    path,
                    null,
                    missing.Kind is null
                        ? $"'{target.DisplayName}' must be mapped or set for this action."
                        : $"'{target.DisplayName}' must be mapped for {missing.Kind}."
                );
        }
    }

    private static void ValidateSources(
        MappingValidationContext context,
        MappingOperation operation,
        KindScope scope,
        ISet<int> flaggedSources
    )
    {
        for (var index = 0; index < context.Mappings.Count; index++)
        {
            var mapping = context.Mappings[index];
            var target = context.Catalog.FindTarget(mapping.TargetKey);
            var source = context.Sources.Component(mapping.ComponentId);
            if (flaggedSources.Contains(index) || target is null || source is null || source.InRepeater)
                continue;
            var kinds = RequiredKinds(target, operation.Name, scope.Kinds);
            if (kinds is null || IsAlwaysAnswered(source, kinds, scope.Analyzer))
                continue;
            flaggedSources.Add(index);
            context.Add(
                IssueCodes.MappingSourceNotRequired,
                $"/mappings/{index}/componentId",
                source.Component.Id,
                "This field feeds a required target: make it required for every client kind that needs it."
            );
        }
    }

    // Null si la operación no exige el destino; si no, los Kind en los que lo exige.
    private static IReadOnlySet<string>? RequiredKinds(
        MappingTarget target,
        string operation,
        IReadOnlySet<string> kinds
    )
    {
        HashSet<string>? result = null;
        foreach (var requirement in target.RequiredFor)
        {
            if (requirement.Operation != operation)
                continue;
            if (requirement.Kind is null)
                return kinds;
            if (kinds.Contains(requirement.Kind))
                (result ??= new HashSet<string>(StringComparer.Ordinal)).Add(requirement.Kind);
        }
        return result;
    }

    private static bool IsAlwaysAnswered(
        MappingSource source,
        IReadOnlySet<string> kinds,
        KindConditionAnalyzer analyzer
    )
    {
        var component = source.Component;
        var required =
            component.Required
            || (component.RequiredWhen is not null && analyzer.HoldsFor(component.RequiredWhen, kinds));
        return required
            && analyzer.HoldsFor(source.Page.VisibleWhen, kinds)
            && analyzer.HoldsFor(source.Section.VisibleWhen, kinds)
            && analyzer.HoldsFor(component.VisibleWhen, kinds);
    }
}
