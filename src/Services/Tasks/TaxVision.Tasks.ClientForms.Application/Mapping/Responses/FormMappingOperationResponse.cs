using TaxVision.DataCollection.Engine.Mapping;

namespace TaxVision.Tasks.ClientForms.Application.Mapping.Responses;

public sealed record FormMappingOperationResponse(
    string Name,
    string? DiscriminatorTargetKey,
    IReadOnlyDictionary<string, string> ConfigTargets
)
{
    public static FormMappingOperationResponse From(MappingOperation operation) =>
        new(operation.Name, operation.DiscriminatorTargetKey, operation.ConfigTargets);
}
