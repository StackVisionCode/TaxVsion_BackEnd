namespace TaxVision.Signature.Application.Requests.Commands.Cancel;

public sealed record CancelSignatureRequestCommand(
    Guid TenantId,
    Guid SignatureRequestId,
    Guid CanceledByUserId,
    string? Reason
);
