namespace TaxVision.Auth.Application.Abstractions;

/// <summary>TOTP (RFC 6238): secretos Base32, códigos de 6 dígitos, paso de 30 s, ventana ±1.</summary>
public interface ITotpService
{
    /// <summary>Genera un secreto aleatorio de 20 bytes codificado en Base32.</summary>
    string GenerateSecret();

    string BuildOtpAuthUri(string accountName, string base32Secret, string issuer);

    bool ValidateCode(string base32Secret, string code, DateTime utcNow);
}

/// <summary>Cifrado simétrico (AES-GCM) para secretos TOTP en reposo.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    /// <summary>Devuelve null si el ciphertext es inválido o la clave no corresponde.</summary>
    string? Unprotect(string ciphertext);
}
