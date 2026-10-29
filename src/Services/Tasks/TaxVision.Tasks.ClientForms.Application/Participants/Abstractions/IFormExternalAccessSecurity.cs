namespace TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;

public sealed record FormOpaqueToken(string RawToken, string TokenHash, string TokenLast4);

public sealed record FormProtectedToken(
    string RawToken,
    string TokenHash,
    string TokenLast4,
    string TokenCipher,
    short TokenKeyVersion
);

public sealed record FormProtectedEmail(string Cipher, short KeyVersion, string LookupHash);

public sealed record FormReEncryptedToken(string Cipher, short KeyVersion);

public interface IFormExternalAccessSecurity
{
    FormOpaqueToken GenerateToken();
    FormProtectedToken GenerateProtectedToken();
    string HashToken(string rawToken);
    string UnprotectToken(string tokenCipher, short keyVersion);
    string GenerateOtp();
    string HashOtp(Guid tenantId, Guid sourceId, string email, string code);
    bool VerifyOtp(Guid tenantId, Guid sourceId, string email, string code, string storedHash);
    FormProtectedEmail ProtectEmail(Guid tenantId, string email);
    string UnprotectEmail(string emailCipher, short keyVersion);

    // Re-cifrado para la rotación (X-9): descifran con la versión del blob y reescriben con la actual.
    FormProtectedEmail ReEncryptEmail(Guid tenantId, string emailCipher, short fromKeyVersion);
    FormReEncryptedToken ReEncryptToken(string tokenCipher, short fromKeyVersion);

    // Un hash por cada versión de clave vigente, para seguir encontrando filas durante la rotación.
    IReadOnlyCollection<string> EmailLookupHashes(Guid tenantId, string email);
}
