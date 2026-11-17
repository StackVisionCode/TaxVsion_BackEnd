namespace TaxVision.Codes.Application.Definitions.SuspendCode;

public sealed record SuspendCodeCommand(
    Guid OwnerTenantId,
    Guid CodeDefinitionId,
    Guid ActorUserId,
    string IdempotencyKey
);
