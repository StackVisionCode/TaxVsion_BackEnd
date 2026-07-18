using BuildingBlocks.Results;

namespace TaxVision.Connectors.Application.Watch;

/// <summary>
/// Renueva la suscripción push de una cuenta antes de que expire. Job-only (WatchRenewalJob) — no
/// hay otro caller. Mismo patrón que IOAuthTokenManager aplicado al refresh de tokens (Fase 4):
/// el port vive en Application, el orquestador (llamadas HTTP + circuit breaker + persistencia)
/// vive en Infrastructure.
/// </summary>
public interface IWatchRenewalService
{
    Task<Result> RenewAsync(Guid subscriptionId, CancellationToken ct = default);
}
