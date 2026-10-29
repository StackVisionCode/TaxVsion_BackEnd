using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Mapping;

public static class MappingCompatibility
{
    private static readonly IReadOnlyDictionary<MappingTargetType, string[]> ComponentTypes = new Dictionary<
        MappingTargetType,
        string[]
    >
    {
        [MappingTargetType.String] = ["text", "textarea"],
        [MappingTargetType.Email] = ["email"],
        [MappingTargetType.Phone] = ["phone"],
        [MappingTargetType.Date] = ["date"],
        [MappingTargetType.Enum] = ["select", "radio"],
        [MappingTargetType.Boolean] = ["yesNo", "checkbox"],
        [MappingTargetType.Address] = ["address"],
        [MappingTargetType.TaxId] = ["taxId"],
        [MappingTargetType.Collection] = [],
    };

    // Una colección se alimenta de una sección repeater, no de un componente.
    public static IReadOnlyList<string> ComponentTypesFor(MappingTargetType targetType) =>
        ComponentTypes.TryGetValue(targetType, out var types) ? types : [];

    public static bool IsCompatible(string componentType, MappingTargetType targetType) =>
        ComponentTypesFor(targetType).Contains(componentType, StringComparer.Ordinal);

    // digitsOnly quitaría el "+" del E.164 y cambiar mayúsculas no tiene sentido fuera del texto libre.
    public static bool AllowsTransform(MappingTargetType targetType, MappingTransform transform) =>
        transform == MappingTransform.None
        || targetType switch
        {
            MappingTargetType.String => true,
            MappingTargetType.Email => transform is MappingTransform.Trim or MappingTransform.Lowercase,
            MappingTargetType.Phone => transform == MappingTransform.Trim,
            _ => false,
        };
}
