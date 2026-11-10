using TaxVision.Calendar.Domain.Booking;

namespace TaxVision.Calendar.Application.Booking.Abstractions;

public interface IBookingPolicyRepository
{
    /// <summary>La política de la oficina (tracked); null si nunca se guardó.</summary>
    Task<BookingPolicy?> GetAsync(Guid tenantId, CancellationToken ct = default);

    void Add(BookingPolicy policy);
}
