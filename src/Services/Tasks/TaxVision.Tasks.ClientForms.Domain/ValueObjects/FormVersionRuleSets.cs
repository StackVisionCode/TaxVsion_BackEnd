using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.ClientForms.Domain.ValueObjects;

// Mapeo y acciones fijados en una versión, con el catálogo de destinos contra el que se validaron.
public sealed record FormVersionRuleSets
{
    private const string EmptySet = "[]";

    private FormVersionRuleSets(string mappingSetJson, string actionSetJson, int targetCatalogVersion)
    {
        MappingSetJson = mappingSetJson;
        ActionSetJson = actionSetJson;
        TargetCatalogVersion = targetCatalogVersion;
    }

    public static FormVersionRuleSets Empty { get; } = new(EmptySet, EmptySet, 0);

    public string MappingSetJson { get; }
    public string ActionSetJson { get; }
    public int TargetCatalogVersion { get; }

    public static Result<FormVersionRuleSets> Create(
        string? mappingSetJson,
        string? actionSetJson,
        int targetCatalogVersion
    )
    {
        if (!IsJsonArray(mappingSetJson) || !IsJsonArray(actionSetJson) || targetCatalogVersion < 0)
            return Result.Failure<FormVersionRuleSets>(FormTemplateErrors.InvalidDefinition);
        return Result.Success(new FormVersionRuleSets(mappingSetJson!, actionSetJson!, targetCatalogVersion));
    }

    private static bool IsJsonArray(string? json) =>
        json is not null
        && json.StartsWith('[')
        && json.EndsWith(']')
        && System.Text.Encoding.UTF8.GetByteCount(json) <= FormDefinitionDocument.MaxBytes;
}
