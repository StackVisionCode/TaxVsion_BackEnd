namespace TaxVision.DataCollection.Engine.Mapping;

public sealed record MappingTargetField(
    string Name,
    MappingTargetType DataType,
    int? MaxLength = null,
    bool Required = false
);
