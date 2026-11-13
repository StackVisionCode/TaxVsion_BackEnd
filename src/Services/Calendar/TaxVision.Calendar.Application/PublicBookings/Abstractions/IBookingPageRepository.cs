using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Application.PublicBookings.Abstractions;

public interface IBookingPageRepository
{
    /// <summary>La página con sus anfitriones y respuestas (tracked); null si no existe en ese tenant.</summary>
    Task<BookingPage?> GetByIdAsync(Guid tenantId, Guid bookingPageId, CancellationToken ct = default);

    /// <summary>La página por su nombre público (tracked); null si no existe en ese tenant.</summary>
    Task<BookingPage?> GetBySlugAsync(Guid tenantId, string slug, CancellationToken ct = default);

    /// <summary>La página activa con ese nombre en alguno de los tenants permitidos; null si no hay.</summary>
    Task<BookingPage?> FindActiveBySlugAsync(
        IReadOnlyCollection<Guid> tenantIds,
        string slug,
        CancellationToken ct = default
    );

    /// <summary>Las páginas del tenant, con anfitriones (sin seguimiento).</summary>
    Task<IReadOnlyList<BookingPage>> ListAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Las páginas donde el usuario es anfitrión activo (tracked).</summary>
    Task<IReadOnlyList<BookingPage>> ListByActiveHostAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    void Add(BookingPage page);
}
