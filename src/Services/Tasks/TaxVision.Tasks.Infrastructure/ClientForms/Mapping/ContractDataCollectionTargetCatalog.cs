using BuildingBlocks.Messaging.ClientFormsIntegrationEvents.Targets;
using TaxVision.DataCollection.Engine.Mapping;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Mapping;

// Traduce el catálogo de contratos al puerto del engine; se arma una vez porque solo cambia con un deploy.
public sealed class ContractDataCollectionTargetCatalog : IDataCollectionTargetCatalog
{
    private readonly MappingCatalogSnapshot _snapshot;

    public ContractDataCollectionTargetCatalog()
        : this(
            CustomerDataCollectionTargets.CatalogVersion,
            CustomerDataCollectionTargets.All,
            CustomerDataCollectionTargets.Operations
        ) { }

    public ContractDataCollectionTargetCatalog(
        int catalogVersion,
        IReadOnlyList<DataCollectionTarget> targets,
        IReadOnlyList<DataCollectionOperationDescriptor> operations
    ) =>
        _snapshot = new MappingCatalogSnapshot(
            catalogVersion,
            targets.Select(ToTarget).ToArray(),
            operations.Select(ToOperation).ToArray()
        );

    public MappingCatalogSnapshot GetSnapshot() => _snapshot;

    private static MappingTarget ToTarget(DataCollectionTarget target) =>
        new(
            target.TargetKey,
            target.Domain,
            target.Entity,
            target.DisplayName,
            ToType(target.DataType),
            ToSensitivity(target.Sensitivity),
            ToStatus(target.Status),
            target.Operations.Select(operation => operation.ToString()).ToArray(),
            target.RequiredFor.Select(item => new MappingRequirement(item.Operation.ToString(), item.Kind)).ToArray(),
            target.RequiredPermissions,
            target.RequiresTenantAdmin,
            target.Constraints.MaxLength,
            target.Constraints.EnumValues,
            target.Constraints.PastOnly,
            target.Fields?.Select(ToField).ToArray(),
            target.TargetSchemaVersion
        );

    private static MappingTargetField ToField(DataCollectionTargetField field) =>
        new(field.Name, ToType(field.DataType), field.MaxLength, field.Required);

    private static MappingOperation ToOperation(DataCollectionOperationDescriptor operation) =>
        new(
            operation.Operation.ToString(),
            operation.RequiredPermissions,
            operation.RequiresTenantAdmin,
            operation.DiscriminatorTargetKey,
            operation.ConfigTargets
        );

    private static MappingTargetType ToType(DataCollectionTargetType type) =>
        type switch
        {
            DataCollectionTargetType.String => MappingTargetType.String,
            DataCollectionTargetType.Date => MappingTargetType.Date,
            DataCollectionTargetType.Email => MappingTargetType.Email,
            DataCollectionTargetType.Phone => MappingTargetType.Phone,
            DataCollectionTargetType.Enum => MappingTargetType.Enum,
            DataCollectionTargetType.Boolean => MappingTargetType.Boolean,
            DataCollectionTargetType.Address => MappingTargetType.Address,
            DataCollectionTargetType.TaxId => MappingTargetType.TaxId,
            DataCollectionTargetType.Collection => MappingTargetType.Collection,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported target type."),
        };

    private static MappingSensitivity ToSensitivity(DataCollectionSensitivity sensitivity) =>
        sensitivity switch
        {
            DataCollectionSensitivity.General => MappingSensitivity.General,
            DataCollectionSensitivity.Personal => MappingSensitivity.Personal,
            DataCollectionSensitivity.Restricted => MappingSensitivity.Restricted,
            _ => throw new ArgumentOutOfRangeException(nameof(sensitivity), sensitivity, "Unsupported sensitivity."),
        };

    private static MappingTargetStatus ToStatus(DataCollectionTargetStatus status) =>
        status switch
        {
            DataCollectionTargetStatus.Active => MappingTargetStatus.Active,
            DataCollectionTargetStatus.Deprecated => MappingTargetStatus.Deprecated,
            DataCollectionTargetStatus.Retired => MappingTargetStatus.Retired,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported target status."),
        };
}
