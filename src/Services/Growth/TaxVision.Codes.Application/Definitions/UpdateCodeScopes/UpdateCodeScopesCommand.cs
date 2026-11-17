using TaxVision.Codes.Application.Definitions.CreateCodeDefinition;

namespace TaxVision.Codes.Application.Definitions.UpdateCodeScopes;

/// <summary>Reemplaza la lista completa de alcances; vale para las próximas cotizaciones.</summary>
public sealed record UpdateCodeScopesCommand(
    Guid OwnerTenantId,
    Guid CodeDefinitionId,
    IReadOnlyCollection<CreateCodeScopeInput> Scopes,
    Guid ActorUserId,
    string IdempotencyKey
);
