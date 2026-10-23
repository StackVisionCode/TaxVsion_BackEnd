namespace TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

using TaxVision.Tasks.ClientForms.Domain.Requests;

public interface IFormAuditLog
{
    void Record(
        Guid tenantId,
        Guid? templateId,
        Guid? requestId,
        string action,
        Guid? actorUserId,
        string? detailsJson = null
    );
    void RecordStaffOutcome(
        Guid tenantId,
        Guid? templateId,
        Guid? requestId,
        string action,
        string outcome,
        Guid actorUserId,
        string? detailsJson = null,
        FormAuditRequestContext? requestContext = null
    );
    void RecordParticipant(Guid tenantId, Guid requestId, string action, FormActor actor, string? detailsJson = null);
    Task RecordAnswersViewedAsync(
        Guid tenantId,
        Guid requestId,
        int revisionNumber,
        Guid actorUserId,
        DateTime nowUtc,
        CancellationToken ct = default
    );
}
