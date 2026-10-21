namespace TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

using TaxVision.Tasks.ClientForms.Domain.Requests;

public interface IFormAuditLog
{
    void Record(Guid tenantId, Guid? templateId, Guid? requestId, string action, Guid? actorUserId, string? detailsJson = null);
    void RecordParticipant(Guid tenantId, Guid requestId, string action, FormActor actor, string? detailsJson = null);
}
