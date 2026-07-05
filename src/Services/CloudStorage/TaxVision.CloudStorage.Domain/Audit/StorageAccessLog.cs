using BuildingBlocks.Domain;

namespace TaxVision.CloudStorage.Domain.Audit;

public sealed class StorageAccessLog : TenantEntity
{
    private StorageAccessLog() { }

    public Guid? FileId { get; private set; }
    public Guid ActorId { get; private set; }
    public string Action { get; private set; } = default!;
    public string Outcome { get; private set; } = default!;
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string CorrelationId { get; private set; } = default!;
    public string? Details { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    public static StorageAccessLog Create(
        Guid tenantId,
        Guid? fileId,
        Guid actorId,
        string action,
        string outcome,
        string? ipAddress,
        string? userAgent,
        string correlationId,
        string? details,
        DateTime nowUtc
    )
    {
        var log = new StorageAccessLog
        {
            FileId = fileId,
            ActorId = actorId,
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
