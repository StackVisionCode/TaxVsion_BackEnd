namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Metadata pública de una clave RSA de firma (para el JWKS): kid + exponente + módulo.
/// El módulo y exponente son base64url — formato JWK estándar RFC 7517.
/// </summary>
public sealed record RsaSigningPublicKey(string Kid, string N, string E, string Alg);

/// <summary>
/// Provee la clave RSA activa (para firmar) y todas las claves publicables (para
/// verificar). Diseño estilo Vault: la privada nunca sale del provider — se firma
/// dentro. Sólo la pública se expone al JWKS.
/// </summary>
public interface IRsaKeyProvider
{
    /// <summary>Devuelve el kid de la clave activa que <see cref="SignAsync"/> usa.</summary>
    string ActiveKid { get; }

    /// <summary>Todas las claves aún publicables (activas + no expiradas) para el JWKS.</summary>
    IReadOnlyList<RsaSigningPublicKey> GetPublicKeys();

    /// <summary>Firma <paramref name="material"/> con RSA-SHA256 usando la clave activa.</summary>
    byte[] SignSha256(byte[] material);
}
