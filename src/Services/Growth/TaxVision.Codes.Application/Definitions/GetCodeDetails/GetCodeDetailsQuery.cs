namespace TaxVision.Codes.Application.Definitions.GetCodeDetails;

public sealed record GetCodeDetailsQuery(
    Guid OwnerTenantId,
    Guid CodeDefinitionId,
    Guid ActorUserId
);
