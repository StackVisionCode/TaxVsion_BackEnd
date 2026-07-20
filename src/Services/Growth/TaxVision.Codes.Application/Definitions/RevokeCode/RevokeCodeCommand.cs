namespace TaxVision.Codes.Application.Definitions.RevokeCode;

public sealed record RevokeCodeCommand(
    Guid OwnerTenantId,
    Guid CodeDefinitionId,
    Guid ActorUserId,
    string IdempotencyKey
);
