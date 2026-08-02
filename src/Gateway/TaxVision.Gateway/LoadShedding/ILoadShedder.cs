namespace TaxVision.Gateway.LoadShedding;

/// <summary>Decide si un request entrante debe rechazarse por sobrecarga de flota (Capa 1, Fase 5
/// del plan de rate limiting). Ver <see cref="LoadShedder"/> para la implementación de referencia.</summary>
public interface ILoadShedder
{
    /// <summary>Debe llamarse una vez por request, después de resolver <paramref name="tenantKey"/>
    /// (tenant_id del JWT o <see cref="TenantConsumptionTracker.AnonymousKey"/>). Devuelve true si
    /// el request debe rechazarse con 503.</summary>
    bool ShouldShed(string tenantKey);

    int RetryAfterSeconds { get; }
}
