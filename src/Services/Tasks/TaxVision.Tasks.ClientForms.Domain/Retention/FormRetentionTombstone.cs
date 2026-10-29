using BuildingBlocks.Domain;

namespace TaxVision.Tasks.ClientForms.Domain.Retention;

/// <summary>
/// X-5 — lápida de una entrega purgada (R-3/R-9). Solo ids, fechas, estado y versión; nunca contenido.
/// Deja constancia de que existió la entrega sin conservar respuestas, sensibles ni adjuntos.
/// </summary>
public sealed class FormRetentionTombstone : TenantEntity
{
    private FormRetentionTombstone() { }

    public Guid FormRequestId { get; private set; }
    public string Rule { get; private set; } = string.Empty;
    public string FinalStatus { get; private set; } = string.Empty;
    public DateTime? RequestedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public DateTime? LastSubmittedAtUtc { get; private set; }
    public int CurrentRevisionNumber { get; private set; }
    public int VersionNumber { get; private set; }
    public DateTime PurgedAtUtc { get; private set; }

    public static FormRetentionTombstone Create(
        Guid tenantId,
        Guid formRequestId,
        string rule,
        string finalStatus,
        DateTime? requestedAtUtc,
        DateTime? completedAtUtc,
        DateTime? cancelledAtUtc,
        DateTime? lastSubmittedAtUtc,
        int currentRevisionNumber,
        int versionNumber,
        DateTime purgedAtUtc
    )
    {
        var tombstone = new FormRetentionTombstone
        {
            FormRequestId = formRequestId,
            Rule = rule,
            FinalStatus = finalStatus,
            RequestedAtUtc = requestedAtUtc,
            CompletedAtUtc = completedAtUtc,
            CancelledAtUtc = cancelledAtUtc,
            LastSubmittedAtUtc = lastSubmittedAtUtc,
            CurrentRevisionNumber = currentRevisionNumber,
            VersionNumber = versionNumber,
            PurgedAtUtc = purgedAtUtc,
        };
        tombstone.SetTenant(tenantId);
        return tombstone;
    }
}
