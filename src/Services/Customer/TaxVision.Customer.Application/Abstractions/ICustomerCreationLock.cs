namespace TaxVision.Customer.Application.Abstractions;

/// <summary>
/// Serializa las altas automáticas de una oficina con el mismo email, para que el detector de la
/// segunda vea a la primera. Vive lo que la transacción en curso: se libera al confirmar o revertir.
/// </summary>
public interface ICustomerCreationLock
{
    /// <summary>Lanza si no obtiene el lock a tiempo: es transitorio y el bus reintenta.</summary>
    Task AcquireAsync(Guid tenantId, string normalizedEmail, CancellationToken ct);
}
