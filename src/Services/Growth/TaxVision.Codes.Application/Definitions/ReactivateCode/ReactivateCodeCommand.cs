namespace TaxVision.Codes.Application.Definitions.ReactivateCode;

public sealed record ReactivateCodeCommand(
    Guid OwnerTenantId,
    Guid CodeDefinitionId,
    Guid ActorUserId,
    string IdempotencyKey
);
