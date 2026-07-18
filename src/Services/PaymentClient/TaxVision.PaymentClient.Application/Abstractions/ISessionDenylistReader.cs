namespace TaxVision.PaymentClient.Application.Abstractions;

/// <summary>Lectura de la denylist de sesiones que Auth escribe en Redis
/// (<c>auth:denylist:sid:{sessionId:N}</c>). PaymentClient nunca deniega sesiones — solo
/// consulta, para cerrar la ventana entre revocación y expiración del access token.</summary>
public interface ISessionDenylistReader
{
    Task<bool> IsSessionDeniedAsync(Guid sessionId, CancellationToken ct = default);
}
