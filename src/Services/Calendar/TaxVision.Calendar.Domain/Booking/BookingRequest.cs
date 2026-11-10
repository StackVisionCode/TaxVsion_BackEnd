using BuildingBlocks.Domain;

namespace TaxVision.Calendar.Domain.Booking;

/// <summary>
/// La reserva de un cliente con su clave de idempotencia: un doble clic o un reintento de red devuelve
/// la misma cita en vez de crear dos. Única por (tenant, cliente, clave).
/// </summary>
public sealed class BookingRequest : TenantEntity
{
    public const int MaxKeyLength = 100;

    private BookingRequest() { }

    public Guid CustomerId { get; private set; }
    public string IdempotencyKey { get; private set; } = default!;
    public Guid AppointmentId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static BookingRequest Create(
        Guid tenantId,
        Guid customerId,
        string idempotencyKey,
        Guid appointmentId,
        DateTime nowUtc
    )
    {
        var request = new BookingRequest
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            IdempotencyKey = idempotencyKey,
            AppointmentId = appointmentId,
            CreatedAtUtc = nowUtc,
        };
        request.SetTenant(tenantId);
        return request;
    }
}
