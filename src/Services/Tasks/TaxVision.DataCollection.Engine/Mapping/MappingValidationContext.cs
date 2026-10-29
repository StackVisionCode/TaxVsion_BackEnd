using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Validation;

namespace TaxVision.DataCollection.Engine.Mapping;

internal sealed class MappingValidationContext(
    FormDefinition definition,
    MappingCatalogSnapshot catalog,
    PublisherCapabilities capabilities,
    MappingSources sources
)
{
    private readonly List<ValidationIssue> _issues = [];

    internal FormDefinition Definition { get; } = definition;
    internal MappingCatalogSnapshot Catalog { get; } = catalog;
    internal PublisherCapabilities Capabilities { get; } = capabilities;
    internal MappingSources Sources { get; } = sources;
    internal IReadOnlyList<FormMapping> Mappings { get; } = definition.Mappings ?? [];

    // Destino → índice del primer mapeo que lo usa.
    internal Dictionary<string, int> MappedTargets { get; } = new(StringComparer.Ordinal);
    internal IReadOnlyList<ValidationIssue> Issues => _issues;

    internal FormMapping? MappingFor(string targetKey) =>
        MappedTargets.TryGetValue(targetKey, out var index) ? Mappings[index] : null;

    internal void Add(string code, string path, string? componentId, string message) =>
        _issues.Add(new ValidationIssue(code, path, componentId, null, message));
}
