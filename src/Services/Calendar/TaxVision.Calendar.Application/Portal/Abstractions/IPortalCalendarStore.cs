using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Booking;

namespace TaxVision.Calendar.Application.Portal.Abstractions;

/// <summary>
/// Lo que el portal lee y escribe: solo citas del cliente (por su <c>CustomerId</c> o como invitado),
/// sus preparadores asignados y sus reservas idempotentes.
/// </summary>
public interface IPortalCalendarStore
{
    /// <summary>Las del cliente que tocan el rango; las series siempre (se expanden después).</summary>
    Task<IReadOnlyList<Appointment>> ListForCustomerAsync(
        Guid tenantId,
        Guid customerId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default
    );

    /// <summary>Con tracking. Null si no existe o no es del cliente: para él son lo mismo.</summary>
    Task<Appointment?> FindForCustomerAsync(
        Guid tenantId,
        Guid customerId,
        Guid appointmentId,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<Guid>> ListAssignedStaffAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    /// <summary>La oficina: empleados activos que ven todos los clientes (<c>customers.view_all</c>).</summary>
    Task<IReadOnlyList<Guid>> ListOfficeStaffAsync(Guid tenantId, CancellationToken ct = default);

    Task<Guid?> FindBookingAsync(Guid tenantId, Guid customerId, string idempotencyKey, CancellationToken ct = default);

    void AddBooking(BookingRequest booking);
}
