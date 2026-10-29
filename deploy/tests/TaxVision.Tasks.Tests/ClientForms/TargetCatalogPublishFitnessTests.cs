using System.Text.Json.Nodes;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents.Targets;
using TaxVision.DataCollection.Engine.Mapping;
using TaxVision.Tasks.Infrastructure.ClientForms.Mapping;

namespace TaxVision.Tasks.Tests.ClientForms;

/// <summary>
/// Ningún destino del catálogo real se puede publicar si deja de estar activo. Primero se prueba que el
/// mismo formulario publica con el destino activo, para que la regla no pase de vacío.
/// </summary>
public sealed class TargetCatalogPublishFitnessTests
{
    public static IEnumerable<object[]> Targets() =>
        CustomerDataCollectionTargets.All.Select(target => new object[] { target.TargetKey });

    [Theory]
    [MemberData(nameof(Targets))]
    public async Task Every_catalog_target_publishes_only_while_active(string targetKey)
    {
        var definition = DefinitionFor(Snapshot(DataCollectionTargetStatus.Active, targetKey).FindTarget(targetKey)!);

        var active = await PublishAsync(definition, DataCollectionTargetStatus.Active, targetKey);
        var deprecated = await PublishAsync(definition, DataCollectionTargetStatus.Deprecated, targetKey);
        var retired = await PublishAsync(definition, DataCollectionTargetStatus.Retired, targetKey);

        Assert.True(active.Published, string.Join(", ", active.Issues.Select(issue => $"{issue.Code} {issue.Path}")));
        Assert.False(deprecated.Published);
        Assert.Contains(deprecated.Issues, issue => issue.Code == "mapping.deprecated_target");
        Assert.False(retired.Published);
        Assert.Contains(retired.Issues, issue => issue.Code == "mapping.unknown_target");
    }

    private static async Task<TaxVision.Tasks.ClientForms.Application.Templates.Responses.PublishFormTemplateOutcome> PublishAsync(
        string definition,
        DataCollectionTargetStatus status,
        string targetKey
    )
    {
        var result = await FormMappingTestDefinitions.PublishAsync(
            FormMappingTestDefinitions.Template(definition),
            FormMappingTestDefinitions.Admin,
            new MemoryFormTemplateVersionRepository(),
            Catalog(status, targetKey)
        );
        return result.Value;
    }

    private static ContractDataCollectionTargetCatalog Catalog(DataCollectionTargetStatus status, string targetKey) =>
        new(
            CustomerDataCollectionTargets.CatalogVersion,
            CustomerDataCollectionTargets
                .All.Select(target => target.TargetKey == targetKey ? target with { Status = status } : target)
                .ToArray(),
            CustomerDataCollectionTargets.Operations
        );

    private static MappingCatalogSnapshot Snapshot(DataCollectionTargetStatus status, string targetKey) =>
        Catalog(status, targetKey).GetSnapshot();

    // Un formulario mínimo cuyo único campo es compatible con el destino.
    private static string DefinitionFor(MappingTarget target)
    {
        var mapping = new JsonObject { ["componentId"] = "fd_source001", ["targetKey"] = target.TargetKey };
        var sections = new JsonArray();
        if (target.DataType == MappingTargetType.Collection)
        {
            sections.Add(RepeaterFor(target, mapping));
        }
        else
        {
            sections.Add(
                Section("sc_main00001", ComponentFor(target, "fd_source001", target.DataType, target.MaxLength))
            );
            if (target.DataType == MappingTargetType.Enum)
                mapping["optionMap"] = new JsonObject(
                    target.AllowedValues.Select((value, index) => KeyValuePair.Create($"o{index}", (JsonNode?)value))
                );
        }
        var definition = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["title"] = "Fitness",
            ["purpose"] = "clientIntake",
            ["presentation"] = "wizard",
            ["pages"] = new JsonArray(
                new JsonObject
                {
                    ["id"] = "pg_abcdefgh",
                    ["title"] = "Page",
                    ["sections"] = sections,
                }
            ),
            ["mappings"] = new JsonArray(mapping),
            ["actions"] = new JsonArray(),
        };
        return definition.ToJsonString();
    }

    private static JsonObject RepeaterFor(MappingTarget target, JsonObject mapping)
    {
        var components = new List<JsonNode>();
        var rowFields = new JsonObject();
        foreach (var (field, index) in target.RowFields.Select((field, index) => (field, index)))
        {
            var id = $"fd_row{index:D6}";
            components.Add(ComponentFor(target, id, field.DataType, field.MaxLength));
            rowFields[id] = field.Name;
        }
        mapping["componentId"] = "sc_rows00001";
        mapping["rowFields"] = rowFields;
        var section = Section("sc_rows00001", [.. components]);
        section["repeat"] = new JsonObject
        {
            ["minRows"] = 0,
            ["maxRows"] = 5,
            ["addLabel"] = "Add",
            ["rowLabel"] = "Row",
        };
        return section;
    }

    private static JsonObject Section(string id, params JsonNode[] components) =>
        new()
        {
            ["id"] = id,
            ["title"] = "Section",
            ["components"] = new JsonArray(components),
        };

    private static JsonObject ComponentFor(MappingTarget target, string id, MappingTargetType type, int? maxLength)
    {
        var (componentType, props) = type switch
        {
            MappingTargetType.String => ("text", new JsonObject { ["maxLength"] = maxLength ?? 80 }),
            MappingTargetType.Date => ("date", new JsonObject { ["max"] = "today" }),
            MappingTargetType.Email => ("email", new JsonObject()),
            MappingTargetType.Phone => ("phone", new JsonObject()),
            MappingTargetType.Boolean => ("yesNo", new JsonObject()),
            MappingTargetType.Address => ("address", new JsonObject { ["country"] = "US" }),
            MappingTargetType.TaxId => ("taxId", new JsonObject { ["kinds"] = new JsonArray("ssn") }),
            MappingTargetType.Enum => ("select", new JsonObject { ["options"] = Options(target) }),
            _ => throw new InvalidOperationException($"No component for {type}."),
        };
        return new JsonObject
        {
            ["id"] = id,
            ["type"] = componentType,
            ["label"] = "Value",
            ["props"] = props,
        };
    }

    private static JsonArray Options(MappingTarget target) =>
        new(
            target
                .AllowedValues.Select(
                    (value, index) => (JsonNode)new JsonObject { ["value"] = $"o{index}", ["label"] = value }
                )
                .ToArray()
        );
}
