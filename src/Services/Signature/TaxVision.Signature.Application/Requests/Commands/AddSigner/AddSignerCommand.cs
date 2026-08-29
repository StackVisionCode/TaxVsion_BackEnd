using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Requests.Commands.AddSigner;

public sealed record AddSignerCommand(
    Guid TenantId,
    Guid SignatureRequestId,
    string Email,
    string FullName,
    string? PhoneNumber = null,
    string? Language = null,
    SignerVerificationMethod? VerificationMethod = null
);
