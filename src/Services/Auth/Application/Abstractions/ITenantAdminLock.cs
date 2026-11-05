namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Serializa, por oficina, las bajas que cuentan administradores activos (regla del último admin).
/// Vive lo que la transacción en curso: se libera al confirmar o revertir.
/// </summary>
public interface ITenantAdminLock
{
    /// <summary>Lanza <see cref="TimeoutException"/> si no obtiene el candado a tiempo: es transitorio.</summary>
    Task AcquireAsync(Guid tenantId, CancellationToken ct);
}
