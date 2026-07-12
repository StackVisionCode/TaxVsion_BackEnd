using BuildingBlocks.Domain;

namespace TaxVision.Customer.Domain.Audit;

/// <summary>
/// Rastro de auditoría para acciones sensibles sobre un customer (ej. revelar el SSN/EIN
/// en claro). Mismo shape que StorageAccessLog de CloudStorage — actor, resultado, IP,
/// user agent y correlationId, sin PII en <see cref="Details"/>.
/// </summary>
public sealed class CustomerAuditLog : TenantEntity
{
    private CustomerAuditLog() { }

    public Guid CustomerId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string Action { get; private set; } = default!;
    public string Outcome { get; private set; } = default!;
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string CorrelationId { get; private set; } = default!;
    public string? Details { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    public static CustomerAuditLog Create(
        Guid tenantId,
        Guid customerId,
        Guid actorUserId,
        string action,
        string outcome,
        string? ipAddress,
        string? userAgent,
        string correlationId,
        string? details,
        DateTime nowUtc
    )
    {
        var log = new CustomerAuditLog
        {
            CustomerId = customerId,
            ActorUserId = actorUserId,
            Action = action,
            Outcome = outcome,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CorrelationId = correlationId,
            Details = details,
            OccurredAtUtc = nowUtc,
        };
        log.SetTenant(tenantId);
        return log;
    }
}
