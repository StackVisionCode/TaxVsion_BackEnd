namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Generación y hash de tokens opacos (refresh, reset, tickets MFA, device trust)
/// y códigos OTP numéricos. Los tokens siempre se persisten hasheados (SHA-256 hex).
/// </summary>
public interface ISecureTokenService
{
    string GenerateToken(int byteLength = 32);
    string GenerateNumericCode(int digits = 6);
    string Hash(string rawToken);
}
