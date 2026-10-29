namespace TaxVision.Tasks.ClientForms.Application.Actions.Commands;

// P-13.2 — Pedir aplicar las respuestas aprobadas al perfil del cliente.
public sealed record ApplyFormProfileUpdateCommand(
    Guid TenantId,
    Guid ByUserId,
    string ActorType,
    bool CanViewAll,
    bool IsTenantAdmin,
    bool IsPlatformAdmin,
    Guid RequestId,
    IReadOnlyList<string>? ComponentIds,
    bool IncludeTaxIdentifier,
    DateTime NowUtc
);
