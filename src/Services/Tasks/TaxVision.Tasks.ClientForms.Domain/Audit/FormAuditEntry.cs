using BuildingBlocks.Domain;

namespace TaxVision.Tasks.ClientForms.Domain.Audit;

public sealed class FormAuditEntry : TenantEntity
{
    private FormAuditEntry() { }

    public Guid? FormTemplateId { get; private set; }
    public Guid? FormRequestId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string Outcome { get; private set; } = string.Empty;
    public Guid? ActorUserId { get; private set; }
    public string ActorType { get; private set; } = string.Empty;
    public Guid? ActorParticipantId { get; private set; }
    public Guid? ActorSessionId { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string? DetailsJson { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    public static FormAuditEntry Create(
        Guid tenantId,
        Guid? formTemplateId,
        Guid? formRequestId,
        string action,
        string outcome,
        Guid? actorUserId,
        string actorType,
        Guid? actorParticipantId,
        Guid? actorSessionId,
        string? ipAddress,
        string? userAgent,
        string correlationId,
        string? detailsJson,
        DateTime occurredAtUtc
    )
    {
        var entry = new FormAuditEntry
        {
            FormTemplateId = formTemplateId,
            FormRequestId = formRequestId,
            Action = action,
            Outcome = outcome,
            ActorUserId = actorUserId,
            ActorType = actorType,
            ActorParticipantId = actorParticipantId,
            ActorSessionId = actorSessionId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CorrelationId = correlationId,
            DetailsJson = detailsJson,
            OccurredAtUtc = occurredAtUtc,
        };
        entry.SetTenant(tenantId);
        return entry;
    }
}
