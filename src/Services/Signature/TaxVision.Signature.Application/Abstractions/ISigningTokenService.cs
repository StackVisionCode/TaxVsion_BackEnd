using BuildingBlocks.Results;

namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Payload firmado que autoriza a un firmante externo a operar sobre una solicitud.
/// La verificación exitosa garantiza integridad del token (HMAC válido) y no-expiración,
/// pero <b>no</b> valida el <c>RevocationEpoch</c> — eso es responsabilidad de la capa
/// Application, que compara el epoch del token contra el actual del aggregate.
/// </summary>
public sealed record SigningTokenPayload(
    Guid TenantId,
    Guid SignatureRequestId,
    Guid SignerId,
    int RevocationEpoch,
    DateTime ExpiresAtUtc,
    string TokenId
);

/// <summary>
/// Emite y verifica tokens compactos firmados para los enlaces públicos de firma.
/// La emisión y la verificación son operaciones puras (no acceden a la BD) — el
/// revocation check y el estado del signer se resuelven en el handler.
/// </summary>
public interface ISigningTokenService
{
    /// <summary>Genera un token firmado (payload URL-safe) que codifica los claims dados.</summary>
    string Issue(SigningTokenPayload payload);

    /// <summary>Construye la URL pública absoluta consumida por el firmante.</summary>
    string BuildPublicUrl(string token);

    /// <summary>
    /// Verifica firma y expiración; devuelve el payload decodificado. Falla si el token
    /// está corrupto, la firma no coincide, o <c>exp</c> ya pasó.
    /// </summary>
    Result<SigningTokenPayload> Verify(string token);
}
