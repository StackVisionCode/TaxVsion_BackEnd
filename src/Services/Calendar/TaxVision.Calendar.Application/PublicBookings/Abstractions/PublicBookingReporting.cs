using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Application.PublicBookings.Abstractions;

/// <summary>Qué reservas de una página ver: por horario de la demo y, si se pide, estado, respuesta u origen.</summary>
public sealed record PublicBookingFilter(
    Guid TenantId,
    Guid PageId,
    DateTime FromUtc,
    DateTime ToUtc,
    PublicBookingStatus? Status = null,
    string? HeardAbout = null,
    BookingEntrySource? Source = null
);

/// <summary>Conteos del rango, agrupados en la base. Un UTM vacío cuenta bajo la clave vacía.</summary>
public sealed record PublicBookingCounts(
    IReadOnlyDictionary<PublicBookingStatus, int> ByStatus,
    IReadOnlyDictionary<string, int> ByHeardAbout,
    IReadOnlyDictionary<BookingEntrySource, int> BySource,
    IReadOnlyDictionary<string, int> ByUtmSource
);
