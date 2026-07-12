using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Requests.Public;

public sealed record VerifyChallengeCommand(
    string Token,
    SignerVerificationMethod Method,
    string Answer,
    string? ClientIp,
    string? UserAgent
);
