using BuildingBlocks.Domain;

namespace TaxVision.Notification.Domain.Notifications;

public sealed class SeatGateNotificationLog : TenantEntity
{
    private SeatGateNotificationLog() { }

    public Guid UserId { get; private set; }
    public Guid SourceEventId { get; private set; }
    public DateTime NotifiedAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }

    public bool IsResolved => ResolvedAtUtc.HasValue;

    public static SeatGateNotificationLog Create(Guid tenantId, Guid userId, Guid sourceEventId, DateTime nowUtc)
    {
        var log = new SeatGateNotificationLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SourceEventId = sourceEventId,
            NotifiedAtUtc = nowUtc,
        };
        log.SetTenant(tenantId);
        return log;
    }

    public bool WasNotifiedWithin(DateTime thresholdUtc) => !IsResolved && NotifiedAtUtc >= thresholdUtc;

    public void Renew(Guid sourceEventId, DateTime nowUtc)
    {
        SourceEventId = sourceEventId;
        NotifiedAtUtc = nowUtc;
        ResolvedAtUtc = null;
    }

    public void Resolve(DateTime nowUtc)
    {
        if (!IsResolved)
            ResolvedAtUtc = nowUtc;
    }
}
