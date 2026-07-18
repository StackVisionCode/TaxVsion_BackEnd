namespace TaxVision.Connectors.Domain.Shared;

/// <summary>
/// Puerto de cifrado para <see cref="EncryptedSecret"/>. Implementación real (AES-GCM 256-bit,
/// 2 master keys activas para rotación) llega en Connectors Fase 3 — no confundir con
/// BuildingBlocks.Security.ISecretProtector (string-a-string, sin versión de key).
/// </summary>
public interface IEncryptedSecretProtector
{
    EncryptedSecret Protect(string plaintext, short? keyVersion = null);

    string Unprotect(EncryptedSecret secret);
}
