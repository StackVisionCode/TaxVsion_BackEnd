namespace TaxVision.Customer.Application.Abstractions;

/// <summary>
/// Abstracción para cifrar identificadores fiscales (SSN/ITIN/EIN, bank accounts) y
/// generar blind indexes para búsqueda por igualdad sin almacenar el texto en claro.
///
/// El blind index es por tenant: dos tenants distintos con el mismo SSN obtienen hashes distintos.
/// </summary>
public interface ISensitiveDataProtector
{
    /// <summary>
    /// Cifra el plain text con AES-GCM y devuelve nonce(12) + ciphertext + tag(16).
    /// </summary>
    byte[] Protect(string plainText);

    /// <summary>
    /// Descifra el blob producido por Protect. Lanza si el tag no valida.
    /// </summary>
    string Unprotect(byte[] cipher);

    /// <summary>
    /// HMAC-SHA256 con clave derivada del tenant. Hex de 64 chars. Determinístico: mismo
    /// (plainText, tenantId) produce siempre el mismo blindIndex.
    /// </summary>
    string ComputeBlindIndex(string plainText, Guid tenantId);

    /// <summary>
    /// Versión de la clave maestra actual, la que se estampa al cifrar (B-9/X-9). Default 1 para
    /// implementaciones de prueba que no rotan.
    /// </summary>
    short CurrentKeyVersion => 1;

    /// <summary>
    /// Blind indexes candidatos para el lookup dual durante la ventana de rotación: el de la clave actual
    /// (idéntico a <see cref="ComputeBlindIndex"/>, siempre primero) y el de la anterior si está configurada.
    /// Sin rotación devuelve un solo candidato, así que la búsqueda se comporta como hoy.
    /// </summary>
    IReadOnlyCollection<string> ComputeBlindIndexCandidates(string plainText, Guid tenantId) =>
        [ComputeBlindIndex(plainText, tenantId)];
}
