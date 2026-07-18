using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Requests.Public;

/// <summary>
/// El firmante público solicita un reto de verificación por un canal concreto. El handler
/// genera el valor en claro (OTP), lo hashea, lo guarda en el aggregate y publica un
/// evento externo genérico que consume el microservicio entregador (SMS gateway, Email,
/// WhatsApp Business, etc.). Signature no habla directamente con ningún proveedor.
/// </summary>
public sealed record IssueVerificationChallengeCommand(
    string Token,
    SignerVerificationMethod Method,
    string? ClientIp,
    string? UserAgent
);
