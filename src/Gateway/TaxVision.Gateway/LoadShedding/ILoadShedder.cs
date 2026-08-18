namespace TaxVision.Gateway.LoadShedding;

/// <summary>Decide si un request entrante debe rechazarse por sobrecarga de flota (Capa 1). Ver
/// <see cref="LoadShedder"/> para la cascada de tres niveles de GW-14.</summary>
public interface ILoadShedder
{
    /// <summary>
    /// Se llama una vez por request, despues de resolver el tenant (tenant_id del JWT o
    /// <see cref="TenantConsumptionTracker.AnonymousKey"/>). Devuelve el motivo del descarte, o
    /// <see cref="SheddingVerdict.Allowed"/> para seguir adelante.
    /// </summary>
    SheddingVerdict Evaluate(string tenantKey, PathString path, bool clientDisconnected);

    int RetryAfterSeconds { get; }
}
