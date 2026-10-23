using TaxVision.DataCollection.Engine.Mapping;

namespace TaxVision.Tasks.ClientForms.Application.Mapping.Responses;

public sealed record FormMappingTargetFieldResponse(
    string Name,
    MappingTargetType DataType,
    int? MaxLength,
    bool Required,
    IReadOnlyList<string> CompatibleComponentTypes
)
{
    public static FormMappingTargetFieldResponse From(MappingTargetField field) =>
        new(
            field.Name,
            field.DataType,
            field.MaxLength,
            field.Required,
            field.DataType == MappingTargetType.Enum ? [] : MappingCompatibility.ComponentTypesFor(field.DataType)
        );
}
