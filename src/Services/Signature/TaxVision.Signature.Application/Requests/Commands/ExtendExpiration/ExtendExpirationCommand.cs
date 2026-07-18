namespace TaxVision.Signature.Application.Requests.Commands.ExtendExpiration;

public sealed record ExtendExpirationCommand(
    Guid TenantId,
    Guid SignatureRequestId,
    Guid ExtendedByUserId,
    int AdditionalHours
);
