namespace TaxVision.DataCollection.Engine.Mapping;

public sealed record MappingCatalogSnapshot(
    int CatalogVersion,
    IReadOnlyList<MappingTarget> Targets,
    IReadOnlyList<MappingOperation> Operations
)
{
    public MappingTarget? FindTarget(string? targetKey) =>
        targetKey is null ? null : Targets.FirstOrDefault(target => target.TargetKey == targetKey);

    public MappingOperation? FindOperation(string name) =>
        Operations.FirstOrDefault(operation => operation.Name == name);

    public IReadOnlyList<string> ReferencedPermissions() =>
        Targets
            .SelectMany(target => target.RequiredPermissions)
            .Concat(Operations.SelectMany(operation => operation.RequiredPermissions))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
