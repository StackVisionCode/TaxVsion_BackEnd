namespace TaxVision.Codes.Application.Definitions.ActivateCode;

public sealed record ActivateCodeCommand(
    Guid OwnerTenantId,
    Guid CodeDefinitionId,
    Guid ActorUserId,
    string IdempotencyKey
);
