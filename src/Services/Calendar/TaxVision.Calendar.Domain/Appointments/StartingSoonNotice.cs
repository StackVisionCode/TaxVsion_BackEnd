using BuildingBlocks.Domain;

namespace TaxVision.Calendar.Domain.Appointments;

/// <summary>
/// Marca de "ya se avisó que esta ocurrencia empieza pronto". Es una fila técnica de deduplicación: el índice
/// único decide quién avisa entre dos réplicas o tras un reinicio. La hora es parte de la clave: si la ocurrencia
/// se mueve, hay aviso nuevo.
/// </summary>
public sealed class StartingSoonNotice : ITenantOwned
{
    private StartingSoonNotice() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid OccurrenceTargetId { get; private set; }
    public DateTime StartUtc { get; private set; }
    public DateTime PublishedAtUtc { get; private set; }

    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public static StartingSoonNotice Create(
        Guid tenantId,
        Guid occurrenceTargetId,
        DateTime startUtc,
        DateTime publishedAtUtc
    )
    {
        var notice = new StartingSoonNotice
        {
            Id = Guid.NewGuid(),
            OccurrenceTargetId = occurrenceTargetId,
            StartUtc = startUtc,
            PublishedAtUtc = publishedAtUtc,
        };
        notice.SetTenant(tenantId);
        return notice;
    }
}
