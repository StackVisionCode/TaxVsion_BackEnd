using TaxVision.Calendar.Application.PublicBookings;
using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Application.PublicBookings.Abstractions;

/// <summary>Reservas públicas. Se leen sin tenant en contexto (visitante anónimo): el tenant va explícito o en el secreto.</summary>
public interface IPublicBookingRepository
{
    /// <summary>La reserva (tracked); null si no existe en ese tenant.</summary>
    Task<PublicBooking?> GetByIdAsync(Guid tenantId, Guid bookingId, CancellationToken ct = default);

    /// <summary>Por el hash del manejo de la solicitud (tracked).</summary>
    Task<PublicBooking?> FindByRequestHandleAsync(byte[] handleHash, CancellationToken ct = default);

    /// <summary>Por el hash del enlace de gestión (tracked).</summary>
    Task<PublicBooking?> FindByManagementTokenAsync(byte[] tokenHash, CancellationToken ct = default);

    Task<PublicBooking?> FindByIdempotencyKeyAsync(
        Guid tenantId,
        Guid pageId,
        string key,
        CancellationToken ct = default
    );

    /// <summary>La pendiente o confirmada del visitante en esa página (tracked).</summary>
    Task<PublicBooking?> FindActiveByRequesterAsync(
        Guid tenantId,
        Guid pageId,
        string requesterKey,
        CancellationToken ct = default
    );

    Task<int> CountRequestsSinceAsync(
        Guid tenantId,
        string requesterKey,
        DateTime sinceUtc,
        CancellationToken ct = default
    );

    /// <summary>Las pendientes con retención vigente que tocan el rango: ocupan el horario de su anfitrión.</summary>
    Task<IReadOnlyList<PublicBooking>> ListHoldsAsync(
        Guid tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        DateTime nowUtc,
        CancellationToken ct = default
    );

    /// <summary>Una página de reservas de la página, por horario de la demo (sin seguimiento).</summary>
    Task<(IReadOnlyList<PublicBooking> Items, int Total)> ListAsync(
        PublicBookingFilter filter,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    Task<PublicBookingCounts> CountAsync(PublicBookingFilter filter, CancellationToken ct = default);

    /// <summary>Las pendientes del anfitrión en ese horario cuya retención ya venció (tracked).</summary>
    Task<IReadOnlyList<PublicBooking>> ListExpiredHoldsAsync(
        Guid tenantId,
        Guid hostUserId,
        DateTime slotStartUtc,
        DateTime nowUtc,
        CancellationToken ct = default
    );

    /// <summary>Las confirmadas que aún no empezaron con ese anfitrión (tracked): las atiende su retiro.</summary>
    Task<IReadOnlyList<PublicBooking>> ListUpcomingByHostAsync(
        Guid tenantId,
        Guid hostUserId,
        DateTime nowUtc,
        CancellationToken ct = default
    );

    /// <summary>Pendientes con la retención vencida, de todos los tenants (job de vencimiento).</summary>
    Task<IReadOnlyList<PublicBookingRef>> ListStaleHoldsAsync(
        DateTime nowUtc,
        int take,
        CancellationToken ct = default
    );

    /// <summary>
    /// Confirmadas con el recordatorio pendiente y su ventana ya abierta, de todos los tenants (job de recordatorio).
    /// </summary>
    Task<IReadOnlyList<PublicBookingRef>> ListDueRemindersAsync(
        DateTime nowUtc,
        int take,
        CancellationToken ct = default
    );

    /// <summary>
    /// Vencidas sin tocar desde <paramref name="unverifiedBeforeUtc"/> y cerradas sin anonimizar desde
    /// <paramref name="closedBeforeUtc"/>, de todos los tenants (job de purga).
    /// </summary>
    Task<IReadOnlyList<PublicBookingRef>> ListPurgeCandidatesAsync(
        DateTime unverifiedBeforeUtc,
        DateTime closedBeforeUtc,
        int take,
        CancellationToken ct = default
    );

    void Add(PublicBooking booking);

    void Remove(PublicBooking booking);
}
