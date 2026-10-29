namespace TaxVision.DataCollection.Engine.Mapping;

// Name coincide con el FormActionType que la ejecuta. ConfigTargets: clave de config de la acción → destino.
public sealed record MappingOperation(
    string Name,
    IReadOnlyList<string> RequiredPermissions,
    bool RequiresTenantAdmin,
    string? DiscriminatorTargetKey,
    IReadOnlyDictionary<string, string> ConfigTargets
);
