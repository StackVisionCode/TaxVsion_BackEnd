using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Domain.Consents;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;

// Identidad de una evidencia: el participante forma parte de ella, igual que en el índice único.
public sealed record FormConsentRecordKey(
    Guid TenantId,
    Guid RequestId,
    string ComponentId,
    FormParticipantKind ParticipantKind,
    Guid? CustomerId,
    Guid? ParticipantId,
    Guid NoticeId,
    int NoticeVersionNumber,
    FormConsentOutcome Outcome
)
{
    public static FormConsentRecordKey For(
        ParticipantContext context,
        Guid requestId,
        string componentId,
        ConsentNoticeVersion version,
        FormConsentOutcome outcome
    ) =>
        new(
            context.TenantId,
            requestId,
            componentId,
            context.Kind,
            context.CustomerId,
            context.ParticipantId,
            version.ConsentNoticeId,
            version.VersionNumber,
            outcome
        );
}
