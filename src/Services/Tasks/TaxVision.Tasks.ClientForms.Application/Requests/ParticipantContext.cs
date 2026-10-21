using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests;

public sealed record ParticipantContext(
    Guid TenantId,
    FormParticipantKind Kind,
    Guid? CustomerId,
    Guid? ParticipantId,
    Guid? BoundRequestId,
    FormActor Actor
)
{
    public static ParticipantContext Portal(Guid tenantId, Guid customerId, Guid portalUserId) => new(
        tenantId,
        FormParticipantKind.Customer,
        customerId,
        null,
        null,
        FormActor.PortalUser(portalUserId)
    );

    public static ParticipantContext External(Guid tenantId, Guid participantId, Guid requestId, Guid sessionId) => new(
        tenantId,
        FormParticipantKind.External,
        null,
        participantId,
        requestId,
        FormActor.ExternalSession(participantId, sessionId)
    );
}
