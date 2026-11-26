using BuildingBlocks.Domain;

namespace TaxVision.Mobile.Domain.Trips;

/// <summary>
/// Quién del personal abrió el trazado GPS de un viaje o exportó el registro de un cliente. Sin IP ni
/// coordenadas. Listar no se registra.
/// </summary>
public sealed class StaffMileageAccessLog : TenantEntity
{
    public const string RouteViewed = "trip.route.viewed";
    public const string TripsExported = "trips.exported";

    private StaffMileageAccessLog() { }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid StaffUserId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? TripId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; }

    public static StaffMileageAccessLog Record(
        Guid tenantId,
        Guid staffUserId,
        Guid customerId,
        Guid? tripId,
        string action,
        DateTime nowUtc
    )
    {
        var entry = new StaffMileageAccessLog
        {
            Id = Guid.NewGuid(),
            StaffUserId = staffUserId,
            CustomerId = customerId,
            TripId = tripId,
            Action = action,
            OccurredAtUtc = nowUtc,
        };
        entry.SetTenant(tenantId);
        return entry;
    }
}
