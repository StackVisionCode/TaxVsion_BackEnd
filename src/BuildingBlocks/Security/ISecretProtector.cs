namespace BuildingBlocks.Security;

/// <summary>
/// Cifrado simétrico de secretos en reposo (tokens OAuth, contraseñas SMTP, API keys,
/// client secrets). Contrato compartido de la plataforma; la implementación por defecto
/// usa AES-256-GCM con la clave <c>Encryption:MasterKey</c>.
/// </summary>
/// <remarks>
/// Regla de seguridad: los valores protegidos nunca deben exponerse en responses ni en
/// logs por encima de Debug.
/// </remarks>
public interface ISecretProtector
{
    /// <summary>Cifra un valor en claro y devuelve <c>base64(nonce || ciphertext || tag)</c>.</summary>
    string Protect(string plaintext);

    /// <summary>
    /// Descifra un valor previamente protegido. Devuelve <c>null</c> si el formato o la clave
    /// no son válidos (no lanza excepción por datos corruptos).
    /// </summary>
    string? Unprotect(string ciphertext);
}
