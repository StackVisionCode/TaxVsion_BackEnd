namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Anuncia una sesión revocada por Pub/Sub de Redis (canal <c>auth:session-revoked</c>) para que
/// Communication empuje el logout en tiempo real a los otros dispositivos del usuario. Best-effort:
/// la revocación autoritativa es la denylist + la BD; esto solo adelanta el aviso. Un fallo de Redis
/// no debe tumbar la revocación, así que la implementación no propaga excepciones.
/// </summary>
public interface ISessionRevocationPublisher
{
    Task PublishRevokedAsync(Guid tenantId, Guid userId, Guid sessionId, string reason, CancellationToken ct = default);
}
