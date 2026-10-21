using BuildingBlocks.Results;

namespace TaxVision.Connectors.Application.Watch;

/// <summary>
/// Renueva la suscripción push de una cuenta antes de que expire. La llaman WatchRenewalJob y los
/// avisos de ciclo de vida de Graph (GraphLifecycleNotificationHandler). Mismo patrón que
/// IOAuthTokenManager aplicado al refresh de tokens (Fase 4):
/// el port vive en Application, el orquestador (llamadas HTTP + circuit breaker + persistencia)
/// vive en Infrastructure.
/// </summary>
public interface IWatchRenewalService
{
    Task<Result> RenewAsync(Guid subscriptionId, CancellationToken ct = default);

    /// <summary>
    /// Crea una suscripción nueva en el proveedor y la guarda en la misma fila. Para cuando el
    /// proveedor borró la anterior (Graph <c>subscriptionRemoved</c>): renovarla daría 404. Los fallos
    /// cuentan igual que los de <see cref="RenewAsync"/> (3 seguidos → Failed + alerta).
    /// </summary>
    Task<Result> RecreateAsync(Guid subscriptionId, CancellationToken ct = default);
}
