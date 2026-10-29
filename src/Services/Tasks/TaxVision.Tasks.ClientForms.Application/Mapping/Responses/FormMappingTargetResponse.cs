using TaxVision.DataCollection.Engine.Mapping;

namespace TaxVision.Tasks.ClientForms.Application.Mapping.Responses;

public sealed record FormMappingTargetResponse(
    string TargetKey,
    string Domain,
    string Entity,
    string DisplayName,
    MappingTargetType DataType,
    MappingSensitivity Sensitivity,
    MappingTargetStatus Status,
    IReadOnlyList<string> Operations,
    IReadOnlyList<FormMappingRequirementResponse> RequiredFor,
    int? MaxLength,
    IReadOnlyList<string> EnumValues,
    bool PastOnly,
    IReadOnlyList<FormMappingTargetFieldResponse> Fields,
    IReadOnlyList<string> CompatibleComponentTypes
)
{
    public static FormMappingTargetResponse From(MappingTarget target) =>
        new(
            target.TargetKey,
            target.Domain,
            target.Entity,
            target.DisplayName,
            target.DataType,
            target.Sensitivity,
            target.Status,
            target.Operations,
            target.RequiredFor.Select(item => new FormMappingRequirementResponse(item.Operation, item.Kind)).ToArray(),
            target.MaxLength,
            target.AllowedValues,
            target.PastOnly,
            target.RowFields.Select(FormMappingTargetFieldResponse.From).ToArray(),
            MappingCompatibility.ComponentTypesFor(target.DataType)
        );
}
