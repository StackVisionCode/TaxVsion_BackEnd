namespace TaxVision.DataCollection.Engine.Mapping;

public sealed record MappingTarget(
    string TargetKey,
    string Domain,
    string Entity,
    string DisplayName,
    MappingTargetType DataType,
    MappingSensitivity Sensitivity,
    MappingTargetStatus Status,
    IReadOnlyList<string> Operations,
    IReadOnlyList<MappingRequirement> RequiredFor,
    IReadOnlyList<string> RequiredPermissions,
    bool RequiresTenantAdmin,
    int? MaxLength = null,
    IReadOnlyList<string>? EnumValues = null,
    bool PastOnly = false,
    IReadOnlyList<MappingTargetField>? Fields = null,
    int TargetSchemaVersion = 1
)
{
    public IReadOnlyList<string> AllowedValues => EnumValues ?? [];
    public IReadOnlyList<MappingTargetField> RowFields => Fields ?? [];
}
