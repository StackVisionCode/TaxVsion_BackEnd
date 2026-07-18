namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Denylist de <c>jti</c> (JWT ID) para revocar tokens públicos individuales antes de
/// su expiración natural. Implementación por defecto vive en Redis con TTL igual a la
/// vida útil del token; en dev/testing existe un stub en memoria.
///
/// <para>
/// Se usa en el flujo público: al aceptar rechazo, revocación por staff o firma
/// completada, el <c>jti</c> se agrega para que el token no pueda reusarse aunque un
/// atacante lo tenga interceptado.
/// </para>
/// </summary>
public interface IJtiDenylist
{
    Task<bool> IsRevokedAsync(string jti, CancellationToken ct = default);

    Task RevokeAsync(string jti, DateTime expiresAtUtc, CancellationToken ct = default);
}
