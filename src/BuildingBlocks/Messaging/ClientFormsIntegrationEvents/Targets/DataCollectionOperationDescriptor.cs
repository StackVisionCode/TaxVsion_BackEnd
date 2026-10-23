namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents.Targets;

// ConfigTargets: propiedad de la config de la acción que puede fijar un destino como constante.
public sealed record DataCollectionOperationDescriptor(
    DataCollectionOperation Operation,
    IReadOnlyList<string> RequiredPermissions,
    bool RequiresTenantAdmin,
    string? DiscriminatorTargetKey,
    IReadOnlyDictionary<string, string> ConfigTargets
);
