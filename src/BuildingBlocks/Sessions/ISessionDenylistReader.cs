namespace BuildingBlocks.Sessions;

/// <summary>
/// RBAC Fase 6 (RBAC_Hardening_Plan.md) — lectura de la denylist de sesiones que Auth escribe en
/// Redis (<c>auth:denylist:sid:{sessionId:N}</c>) al revocar una sesión. Todo servicio salvo Auth
/// solo consulta, nunca deniega — cierra la ventana entre revocación y expiración del access token
/// (hasta 15 min). Vive en el proyecto base (no en BuildingBlocks.Web) para que las capas
/// Infrastructure de cada microservicio puedan implementarla/consumirla sin depender de
/// ASP.NET Core — Auth mismo la implementa en <c>AccessTokenDenylist</c> (que además escribe).
/// </summary>
public interface ISessionDenylistReader
{
    Task<bool> IsSessionDeniedAsync(Guid sessionId, CancellationToken ct = default);
}
