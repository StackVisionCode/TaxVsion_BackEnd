namespace TaxVision.Calendar.Application.Appointments.Abstractions;

/// <summary>
/// Serializa las altas de un mismo organizador dentro de la transacción del handler. Se suelta sola al confirmar o
/// revertir. Si no se consigue a tiempo, lanza <c>ConflictException</c>.
/// </summary>
public interface IOrganizerScheduleLock
{
    Task AcquireAsync(Guid tenantId, Guid organizerUserId, CancellationToken ct);
}
