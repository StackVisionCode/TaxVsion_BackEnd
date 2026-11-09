namespace TaxVision.Customer.Application.Abstractions;

/// <summary>
/// Serializa las altas de una oficina con el mismo email (HTTP, formularios e importación), para que el
/// detector de la segunda vea a la primera. Vive lo que la transacción en curso: se libera al confirmar o revertir.
/// </summary>
public interface ICustomerCreationLock
{
    /// <summary>
    /// Lanza <see cref="TimeoutException"/> si no lo obtiene a tiempo: es transitorio y el bus reintenta.
    /// Pedirlo otra vez en la misma transacción no espera.
    /// </summary>
    Task AcquireAsync(Guid tenantId, string normalizedEmail, CancellationToken ct);
}
