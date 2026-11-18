namespace TaxVision.Customer.Application.Abstractions;

/// <summary>
/// Serializa las altas de una oficina del mismo cliente (HTTP, formularios e importación), para que el detector
/// de la segunda vea a la primera. La llave es el email o, sin email, la de <c>CustomerDuplicateRules.LockKey</c>.
/// Vive lo que la transacción en curso: se libera al confirmar o revertir.
/// </summary>
public interface ICustomerCreationLock
{
    /// <summary>
    /// Lanza <see cref="TimeoutException"/> si no lo obtiene a tiempo: es transitorio y el bus reintenta.
    /// Pedirlo otra vez en la misma transacción no espera.
    /// </summary>
    Task AcquireAsync(Guid tenantId, string key, CancellationToken ct);
}
